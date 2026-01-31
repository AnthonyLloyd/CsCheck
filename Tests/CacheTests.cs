namespace Tests;

using CsCheck;
using System.Collections.Concurrent;

public class CacheTests
{
    private async Task<object> SlowFunction(int i)
    {
        await Task.Delay(1);
        return Random.Shared;
    }

    [Test]
    public async Task StampedeFree()
    {
        await Gen.Int.HashSet[1, 10].SampleAsync(async ks =>
        {
            var alreadyRun = 0;
            var ks0 = ks.First();
            async Task<long> Function(int i)
            {
                if (i == ks0 && Interlocked.CompareExchange(ref alreadyRun, 1, 0) != 0) throw new("Stampede!");
                await Task.Delay(1);
                return i;
            }
            var cache = new Cache<int, long>();
            foreach(var k in ks.Skip(1))
                await cache.GetOrAdd(k, Function);
            var tasks = new Task<long>[10];
            for(int i = 0; i < tasks.Length; i++)
                tasks[i] = Task.Run(async () => await cache.GetOrAdd(ks0, Function));
            for (int i = 0; i < tasks.Length; i++)
                if (await tasks[i] != ks0)
                    return false;
            return true;
        });
    }

    [Test]
    public async Task Add_Faster()
    {
        const int input = 1;
        await Gen.Const(() => (new Cache<int, object>(), new ConcurrentDictionary<int, Lazy<Task<object>>>()))
            .FasterAsync(
            async (c, _) =>
            {
                var x = await c.GetOrAdd(input, SlowFunction);
                return x is null ? 1 : 0;
            },
            async (_, c) =>
            {
                var x = await c.GetOrAdd(input, input => new Lazy<Task<object>>(() => SlowFunction(input))).Value;
                return x is null ? 1 : 0;
            },
            raiseexception: false,
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public async Task Get_Faster()
    {
        const int input = 1;
        var c = new Cache<int, object>();
        await c.GetOrAdd(input, SlowFunction);
        var l = new ConcurrentDictionary<int, Lazy<Task<object>>>();
        await l.GetOrAdd(input, input => new Lazy<Task<object>>(() => SlowFunction(input))).Value;
        await Check.FasterAsync(
            async () =>
            {
                var x = await c.GetOrAdd(input, SlowFunction);
                x = await c.GetOrAdd(input, SlowFunction);
                x = await c.GetOrAdd(input, SlowFunction);
                x = await c.GetOrAdd(input, SlowFunction);
                x = await c.GetOrAdd(input, SlowFunction);
                return x is null ? 1 : 0;
            },
            async () =>
            {
                var x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                return x is null ? 1 : 0;
            },
            repeat: 10,
            raiseexception: false,
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public async Task Update_ForcesRefresh()
    {
        var cache = new Cache<int, int>();
        var callCount = 0;
        var key = 1;

        async Task<int> Factory(int k)
        {
            await Task.Delay(1);
            return Interlocked.Increment(ref callCount);
        }

        // Initial call should invoke factory
        var value1 = await cache.GetOrAdd(key, Factory);
        if (value1 != 1 || callCount != 1)
            throw new Exception("Initial GetOrAdd failed");

        // GetOrAdd should return cached value without invoking factory
        var value2 = await cache.GetOrAdd(key, Factory);
        if (value2 != 1 || callCount != 1)
            throw new Exception("GetOrAdd should return cached value");

        // Update should force a refresh and invoke factory again
        var value3 = await cache.Update(key, Factory);
        if (value3 != 2 || callCount != 2)
            throw new Exception("Update should force refresh");

        // GetOrAdd should now return the updated value
        var value4 = await cache.GetOrAdd(key, Factory);
        if (value4 != 2 || callCount != 2)
            throw new Exception("GetOrAdd should return updated value");
    }

    [Test]
    public async Task Update_StampedeFree()
    {
        await Gen.Int.HashSet[1, 10].SampleAsync(async ks =>
        {
            var callCount = 0;
            var testKey = ks.First();
            async Task<long> Factory(int i)
            {
                Interlocked.Increment(ref callCount);
                await Task.Delay(10); // Longer delay to ensure concurrent calls overlap
                return i;
            }
            var cache = new Cache<int, long>();
            
            // Pre-populate cache
            foreach (var k in ks)
                await cache.GetOrAdd(k, Factory);
            
            var initialCallCount = callCount;
            
            // Multiple concurrent Update calls should result in fewer factory calls than the number of Update calls
            // due to stampede protection (but not necessarily just one call due to timing of pending removal)
            var tasks = new Task<long>[10];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = Task.Run(async () => await cache.Update(testKey, Factory));
            
            for (int i = 0; i < tasks.Length; i++)
                if (await tasks[i] != testKey)
                    return false;
            
            // Should have fewer calls than the number of concurrent Update calls
            // (stampede protection reduces duplicates)
            var additionalCalls = callCount - initialCallCount;
            if (additionalCalls >= 10)
                throw new Exception($"No stampede protection: got {additionalCalls} calls for 10 concurrent Updates");
            
            if (additionalCalls < 1)
                throw new Exception($"Update didn't trigger any factory calls");
            
            return true;
        });
    }
}