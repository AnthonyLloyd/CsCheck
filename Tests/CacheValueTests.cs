namespace Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using CsCheck;

public class CacheValue<T>
{
    private long _timestamp; // 0 not set, -1 setting, +ive value timestamp, -ive error timestamp
    private Exception _exception = null!;
    private T _value = default!;

    public ValueTask<T> GetAsync<K>(Func<K, Task<T>> factory, K key)
        => Volatile.Read(ref _timestamp) > 0 ? new(_value) : CreateOrException(factory, key);

    private async ValueTask<T> CreateOrException<K>(Func<K, Task<T>> factory, K key)
    {
        if (Interlocked.CompareExchange(ref _timestamp, -1, 0) == 0)
        {
            try
            {
                _value = await factory(key);
                Volatile.Write(ref _timestamp, Stopwatch.GetTimestamp());
                return _value;
            }
            catch (Exception e)
            {
                _exception = e;
                Volatile.Write(ref _timestamp, Math.Min(-Stopwatch.GetTimestamp(), -2));
                throw;
            }
        }
        while (Volatile.Read(ref _timestamp) == -1) await Task.Yield();
        return Volatile.Read(ref _timestamp) > 0 ? _value : throw _exception;
    }

    public long Timestamp
    {
        get
        {
            var t = Volatile.Read(ref _timestamp);
            return t > 0 ? t
                : t < -1 ? -t
                : t;
        }
        set
        {
            var t = Volatile.Read(ref _timestamp);
            if (t > 0)
                _timestamp = value;
            else if (t < -1)
                _timestamp = -value;
        }
    }
}

public sealed class CacheValueExpiry<T> : CacheValue<T>
{
    public int LastUsed;
}


public class CacheValueTests
{
    [Test]
    public async Task StampedeFree()
    {
        var alreadyRun = false;
        async Task<long> Function(int i)
        {
            if (alreadyRun) throw new();
            alreadyRun = true;
            await Task.Delay(100);
            return i;
        }
        for (int i = 0; i < 100; i++)
        {
            alreadyRun = false;
            var cache = new CacheValue<long>();
            var t1 = cache.GetAsync(Function, 1);
            var t2 = cache.GetAsync(Function, 2);
            var r1 = await t1;
            var r2 = await t2;
            await Assert.That(r1).IsEqualTo(r2);
        }
    }

    private async Task<object> SlowFunction(object i)
    {
        await Task.Delay(1);
        return Random.Shared;
    }

    [Test]
    public async Task Add_Faster()
    {
        var input = new object();
        await Check.FasterAsync(
            async () =>
            {
                var c = new CacheValue<object>();
                var x = await c.GetAsync(SlowFunction, input);
                return x is null ? 1 : 0;
            },
            async () =>
            {
                var c = new Lazy<Task<object>>(() => SlowFunction(input));
                var x = await c.Value;
                return x is null ? 1 : 0;
            },
            raiseexception: false,
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public async Task Get_Faster()
    {
        var input = new object();
        var c = new ConcurrentDictionary<object, CacheValue<object>>();
        await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
        var l = new ConcurrentDictionary<object, Lazy<Task<object>>>();
        await l.GetOrAdd(input, input => new Lazy<Task<object>>(() => SlowFunction(input))).Value;
        await Check.FasterAsync(
            async () =>
            {
                var x = await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
                return x is null ? 1 : 0;
            },
            async () =>
            {
                var x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                return x is null ? 1 : 0;
            },
            repeat: 100,
            raiseexception: false,
            writeLine: TUnitX.WriteLine);
    }
}
