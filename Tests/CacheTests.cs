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
}