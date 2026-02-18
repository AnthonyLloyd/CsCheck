namespace Tests;

using CsCheck;
using System.Collections.Concurrent;

public class CacheTests
{
    [Test]
    public void Cache_GetOrAdd_ModelBased()
    {
        static void CacheTryAdd(Cache<int, byte> cache, int key, byte value)
            => cache.GetOrAdd(key, _ => Task.FromResult(value)).AsTask().GetAwaiter().GetResult();

        Gen.Select(Gen.Int, Gen.Byte).Array
        .Select(kvs =>
        {
            var cache = new Cache<int, byte>();
            var dictionary = new Dictionary<int, byte>();
            foreach (var (key, value) in kvs)
            {
                CacheTryAdd(cache, key, value);
                dictionary.TryAdd(key, value);
            }
            return (cache, dictionary);
        })
        .SampleModelBased(
            Gen.Select(Gen.Int, Gen.Byte).Operation<Cache<int, byte>, Dictionary<int, byte>>(
                (cache, kv) => CacheTryAdd(cache, kv.Item1, kv.Item2),
                (dictionary, kv) => dictionary.TryAdd(kv.Item1, kv.Item2))
        );
    }

    [Test]
    public async Task Cache_GetOrAdd_StampedeFree()
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

    private static async Task<object> SlowFunction(int i)
    {
        await Task.Delay(1);
        return Random.Shared;
    }

    [Test]
    public async Task Cache_GetOrAdd_Add_Faster()
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
    public async Task Cache_GetOrAdd_Get_Faster()
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
    public async Task Cache_Update_ForcesRefresh()
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
    public async Task Cache_Update_StampedeFree()
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

    [Test]
    public async Task Cache_Compact_RemovesEntries()
    {
        var cache = new Cache<int, int>();
        for (int i = 0; i < 10; i++)
            await cache.GetOrAdd(i, k => Task.FromResult(k * 10));
        await Assert.That(cache.Count).IsEqualTo(10);
        cache.Compact(e => e.Key % 2 == 0);
        await Assert.That(cache.Count).IsEqualTo(5);
        // Kept entries are still accessible
        await Assert.That(await cache.GetOrAdd(0, _ => Task.FromResult(-1))).IsEqualTo(0);
        await Assert.That(await cache.GetOrAdd(4, _ => Task.FromResult(-1))).IsEqualTo(40);
        // Removed entries invoke factory again
        await Assert.That(await cache.GetOrAdd(1, _ => Task.FromResult(-1))).IsEqualTo(-1);
        await Assert.That(await cache.GetOrAdd(3, _ => Task.FromResult(-1))).IsEqualTo(-1);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_ReturnsCachedValue()
    {
        using var cache = new RefreshingCache<int, int>(TimeSpan.FromMinutes(10));
        var callCount = 0;
        Task<int> Factory(int _) => Task.FromResult(Interlocked.Increment(ref callCount));
        var first = await cache.GetOrAdd(1, Factory);
        var second = await cache.GetOrAdd(1, Factory);
        await Assert.That(first).IsEqualTo(1);
        await Assert.That(second).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_MultipleKeys()
    {
        using var cache = new RefreshingCache<int, int>(TimeSpan.FromMinutes(10));
        static Task<int> Factory(int k) => Task.FromResult(k * 100);
        var tasks = Enumerable.Range(0, 20)
            .Select(k => cache.GetOrAdd(k, Factory).AsTask())
            .ToArray();
        var results = await Task.WhenAll(tasks);
        for (int i = 0; i < 20; i++)
            await Assert.That(results[i]).IsEqualTo(i * 100);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_ConcurrentSameKey()
    {
        using var cache = new RefreshingCache<int, int>(TimeSpan.FromMinutes(10));
        var callCount = 0;
        async Task<int> Factory(int k)
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(10);
            return k;
        }
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(async () => await cache.GetOrAdd(1, Factory)))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        foreach (var r in results)
            await Assert.That(r).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_RefreshesAfterExpiry()
    {
        var duration = TimeSpan.FromMilliseconds(10);
        using var cache = new RefreshingCache<int, int>(duration);
        var callCount = 0;
        Task<int> Factory(int _) => Task.FromResult(Interlocked.Increment(ref callCount));
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Task.Delay(duration * 2);
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(2);
        await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_EagerRefresh()
    {
        var duration = TimeSpan.FromMilliseconds(50_000);
        using var cache = new RefreshingCache<int, int>(duration, eagerRefreshRatio: 0.0001);
        var callCount = 0;
        Task<int> Factory(int _) => Task.FromResult(Interlocked.Increment(ref callCount));
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(2);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_FailsafeReturnsStale()
    {
        var duration = TimeSpan.FromMilliseconds(10);
        using var cache = new RefreshingCache<int, int>(duration);
        var callCount = 0;
        Task<int> Factory(int _) => Interlocked.Increment(ref callCount) == 1 ? Task.FromResult(1) : throw new("Factory failed");
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Task.Delay(duration * 2);
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task RefreshingCache_GetOrAdd_SoftTimeoutReturnsStale()
    {
        var duration = TimeSpan.FromMilliseconds(10);
        using var cache = new RefreshingCache<int, int>(duration, softTimeout: TimeSpan.FromMilliseconds(5));
        var callCount = 0;
        async Task<int> Factory(int _)
        {
            var c = Interlocked.Increment(ref callCount);
            if (c > 1) await Task.Delay(10_000);
            return c;
        }
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
        await Task.Delay(duration * 2);
        await Assert.That(await cache.GetOrAdd(1, Factory)).IsEqualTo(1);
    }
}