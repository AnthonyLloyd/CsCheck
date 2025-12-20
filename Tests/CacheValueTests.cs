namespace Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using CsCheck;

public class CacheValue<T>
{
    private long _timestamp; // 0 not run, -1 running, +ive value timestamp + 2, -ive error -(timestamp + 2)
    private Exception _exception = null!;
    private T _value = default!;

    public ValueTask<T> GetAsync<K>(Func<K, Task<T>> factory, K key)
        => Volatile.Read(ref _timestamp) > 0 ? new(_value) : new(UpdateAsync(factory, key));

    public async Task<T> UpdateAsync<K>(Func<K, Task<T>> factory, K key)
    {
        if (Interlocked.CompareExchange(ref _timestamp, -1, 0) == 0)
        {
            try
            {
                _value = await factory(key);
                Volatile.Write(ref _timestamp, Stopwatch.GetTimestamp() + 2);
                return _value;
            }
            catch (Exception e)
            {
                _exception = e;
                Volatile.Write(ref _timestamp, -(Stopwatch.GetTimestamp() + 2));
                throw;
            }
        }
        while (Volatile.Read(ref _timestamp) == -1) await Task.Yield();
        return Volatile.Read(ref _timestamp) > 0 ? _value : throw _exception;
    }

    /// <summary>Sets back to not run if not currently running. Will cause next GetAsync to refetch.</summary>
    public void Invalidate()
    {
        var timestamp = Volatile.Read(ref _timestamp);
        if (timestamp != -1)
            Interlocked.CompareExchange(ref _timestamp, 0, timestamp);
    }

    /// <summary>-1 = not run, -2 = running, else a real Timestamp.</summary>
    public long Timestamp
    {
        get
        {
            var t = Volatile.Read(ref _timestamp);
            return t > 0 ? t - 2
                : t < -1 ? -t - 2
                : t - 1;
        }
        set
        {
            if (value < 0) throw new ArgumentException("Timestamp can't be set negative");
            var t = Volatile.Read(ref _timestamp);
            if (t > 0)
                _timestamp = value + 2;
            else if (t < -1)
                _timestamp = -(value + 2);
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
        await Gen.Int.SampleAsync(async i =>
        {
            var alreadyRun = false;
            async Task<long> Function(int i)
            {
                if (alreadyRun) throw new();
                alreadyRun = true;
                await Task.Delay(1);
                return i;
            }
            var cache = new CacheValue<long>();
            var t1 = cache.GetAsync(Function, -i);
            var t2 = cache.GetAsync(Function, i);
            var r1 = await t1;
            var r2 = await t2;
            return r1 == r2;
        });
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
                return x == input ? 1 : 0;
            },
            async () =>
            {
                var c = new Lazy<Task<object>>(() => SlowFunction(input));
                var x = await c.Value;
                return x == input ? 1 : 0;
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
                x = await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
                x = await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
                x = await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
                x = await c.GetOrAdd(input, _ => new()).GetAsync(SlowFunction, input);
                return x == input ? 1 : 0;
            },
            async () =>
            {
                var x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                x = await l.GetOrAdd(input, (input, f) => new Lazy<Task<object>>(() => f(input)), SlowFunction).Value;
                return x == input ? 1 : 0;
            },
            repeat: 100,
            raiseexception: false,
            writeLine: TUnitX.WriteLine);
    }
}
