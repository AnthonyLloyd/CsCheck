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
    public async Task GetOrAdd_StampedeFree()
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
    public async Task GetOrAdd_Add_Faster()
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
    public async Task GetOrAdd_Get_Faster()
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
        const int key = 1;

        async Task<int> Factory(int _)
        {
            await Task.Delay(1);
            return Interlocked.Increment(ref callCount);
        }

        // Initial call should invoke factory
        await Assert.That(await cache.GetOrAdd(key, Factory)).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(1);

        // GetOrAdd should return cached value without invoking factory
        await Assert.That(await cache.GetOrAdd(key, Factory)).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(1);

        // Update should force a refresh and invoke factory again
        await Assert.That(await cache.Update(key, Factory)).IsEqualTo(2);
        await Assert.That(callCount).IsEqualTo(2);

        // GetOrAdd should now return the updated value
        await Assert.That(await cache.GetOrAdd(key, Factory)).IsEqualTo(2);
        await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task Update_StampedeFree()
    {
        await Gen.Int.HashSet[1, 10].SampleAsync(async ks =>
        {
            var alreadyRun = 0;
            var callCount = 0;
            var ks0 = ks.First();
            async Task<long> Factory(int i)
            {
                Interlocked.Increment(ref callCount);
                if (Interlocked.CompareExchange(ref alreadyRun, 1, 0) != 0) throw new("Stampede!");
                await Task.Delay(1);
                Interlocked.Decrement(ref alreadyRun);
                return i;
            }
            var cache = new Cache<int, long>();
            foreach (var k in ks)
                await cache.GetOrAdd(k, Factory);
            var tasks = new Task<long>[10];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = Task.Run(async () => await cache.Update(ks0, Factory));
            for (int i = 0; i < tasks.Length; i++)
                if (await tasks[i] != ks0)
                    return false;
            if (callCount == ks.Count)
                throw new("Update didn't trigger any factory calls");
            return true;
        });
    }
}