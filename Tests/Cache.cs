namespace Tests;

using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

public sealed class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    private sealed class Pending { internal required K Key; internal required Task<V> Value; internal Pending? Next; }
    private readonly Lock _lock = new();
    private int _count;
    private int _version;
    private Entry[] _entries;
    private Pending? _pending;

    public Cache() => _entries = new Entry[2];
    public Cache(int capacity) => _entries = new Entry[capacity < 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)capacity)];

    public Cache(IEnumerable<KeyValuePair<K, V>> items)
    {
        _entries = new Entry[2];
        foreach (var (k, v) in items) AddOrUpdate(k, v);
    }

    public int Count => _count;

    private void AddOrUpdate(K key, V value)
    {
        var hashCode = key.GetHashCode();
        var entries = _entries;
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0 && !key.Equals(entries[i].Key)) i = entries[i].Next;
        if (i >= 0)
        {
            entries[i].Value = value;
            return;
        }
        i = _count;
        if (entries.Length == i) entries = Resize();
        var bucketIndex = hashCode & (entries.Length - 1);
        entries[i].Next = entries[bucketIndex].Bucket - 1;
        entries[i].Key = key;
        entries[i].Value = value;
        entries[bucketIndex].Bucket = ++_count;
        _version++;
        _entries = entries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Entry[] Resize()
    {
        var oldEntries = _entries;
        var newEntries = new Entry[oldEntries.Length * 2];
        for (int i = 0; i < oldEntries.Length;)
        {
            var bucketIndex = oldEntries[i].Key.GetHashCode() & (newEntries.Length - 1);
            newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
            newEntries[i].Key = oldEntries[i].Key;
            newEntries[i].Value = oldEntries[i].Value;
            newEntries[bucketIndex].Bucket = ++i;
        }
        return newEntries;
    }

    public ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
        => GetOrAdd(key, factory, static (k, f) => f(k));

    public ValueTask<V> GetOrAdd<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        var version = Volatile.Read(ref _version);
        var entries = Volatile.Read(ref _entries);
        var hashCode = key.GetHashCode();
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            if (key.Equals(entries[i].Key)) return new(entries[i].Value);
            i = entries[i].Next;
        }
        lock (_lock)
        {
            if (_version != version)
            {
                entries = _entries;
                i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
                while (i >= 0)
                {
                    if (key.Equals(entries[i].Key)) return new(entries[i].Value);
                    i = entries[i].Next;
                }
            }
            return new(GetOrAddPending(key, state, factory).Value);
        }
    }

    public Task<V> Update(K key, Func<K, Task<V>> factory)
        => Update(key, factory, static (k, f) => f(k));

    public Task<V> Update<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        lock (_lock)
            return GetOrAddPending(key, state, factory).Value;
    }

    public void Compact(Func<KeyValuePair<K, V>, bool> keep)
    {
        lock (_lock)
        {
            var oldEntries = _entries;
            var oldCount = _count;
            int newCount = 0;
            var newEntries = new Entry[oldCount < 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)oldCount)];
            for (int i = 0; i < oldCount; i++)
            {
                if (keep(new(oldEntries[i].Key, oldEntries[i].Value)))
                {
                    var bucketIndex = oldEntries[i].Key.GetHashCode() & (newEntries.Length - 1);
                    newEntries[newCount].Next = newEntries[bucketIndex].Bucket - 1;
                    newEntries[newCount].Key = oldEntries[i].Key;
                    newEntries[newCount].Value = oldEntries[i].Value;
                    newEntries[bucketIndex].Bucket = ++newCount;
                }
            }
            _count = newCount;
            _version++;
            _entries = newEntries;
        }
    }

    private Pending GetOrAddPending<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        var pending = _pending;
        if (pending is null)
            return _pending = CreatePending(key, state, factory);
        while (true)
        {
            if (key.Equals(pending.Key)) return pending;
            if (pending.Next is null)
                return pending.Next = CreatePending(key, state, factory);
            pending = pending.Next;
        }
    }

    private Pending CreatePending<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        Pending pending = null!;
        pending = new Pending
        {
            Key = key,
            Value = Task.Run(async () =>
            {
                try
                {
                    var value = await factory(key, state);
                    lock (_lock)
                    {
                        RemovePending(pending);
                        AddOrUpdate(key, value);
                    }
                    return value;
                }
                catch
                {
                    lock (_lock)
                        RemovePending(pending);
                    throw;
                }
            }),
        };
        return pending;
    }

    private void RemovePending(Pending remove)
    {
        var pending = _pending;
        if (ReferenceEquals(pending, remove))
            _pending = remove.Next;
        else
        {
            while (!ReferenceEquals(pending!.Next, remove))
                pending = pending.Next;
            pending.Next = remove.Next;
        }
    }

    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return new(_entries[i].Key, _entries[i].Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class RefreshingCache<K, V> : IDisposable where K : IEquatable<K>
{
    private readonly Cache<K, (long Timestamp, V Value)> _cache = new();
    private readonly long _durationTicks, _eagerRefreshTicks;
    private readonly TimeSpan _softTimeout;
    private readonly Timer? _timer;

    public RefreshingCache(TimeSpan duration, double eagerRefreshRatio = 0.75,
        TimeSpan? softTimeout = null, TimeSpan? cleanupPeriod = null)
    {
        _durationTicks = (long)(duration.TotalSeconds * Stopwatch.Frequency);
        _eagerRefreshTicks = (long)(_durationTicks * Math.Clamp(eagerRefreshRatio, 0.0, 1.0));
        _softTimeout = softTimeout ?? TimeSpan.Zero;
        if (cleanupPeriod.HasValue) _timer = new Timer(Cleanup, null, cleanupPeriod.Value, cleanupPeriod.Value);
    }

    private void Cleanup(object? _)
    {
        var now = Stopwatch.GetTimestamp();
        _cache.Compact(e => now - e.Value.Timestamp < _durationTicks * 2);
    }

    public async ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
    {
        var now = Stopwatch.GetTimestamp();
        var result = await _cache.GetOrAdd(key, factory, CallFactory);
        var age = now - result.Timestamp;
        if (age >= _durationTicks)
            result = await RefreshWithTimeout(key, factory, result);
        else if (age >= _eagerRefreshTicks)
            _ = _cache.Update(key, factory, CallFactory);
        return result.Value;
    }

    private async Task<(long Timestamp, V Value)> RefreshWithTimeout(K key, Func<K, Task<V>> factory, (long Timestamp, V Value) stale)
    {
        var updateTask = _cache.Update(key, factory, CallFactory);
        if (_softTimeout > TimeSpan.Zero)
        {
            var completed = await Task.WhenAny(updateTask, Task.Delay(_softTimeout));
            if (completed != updateTask)
                return stale;
        }
        try
        {
            return await updateTask;
        }
        catch
        {
            return stale;
        }
    }

    private static async Task<(long, V)> CallFactory(K key, Func<K, Task<V>> factory)
    {
        var value = await factory(key);
        return (Stopwatch.GetTimestamp(), value);
    }

    public void Dispose() => _timer?.Dispose();
}