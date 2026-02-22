namespace Tests;

using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>A thread-safe cache with lock-free reads and stampede-free population.
/// Concurrent requests for the same key share a single pending task, avoiding redundant factory calls.
/// Uses power-of-two sized buckets for efficient bitwise index calculation and cache-friendly array storage.</summary>
public sealed class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    private sealed class Pending { internal required K Key; internal required Task<V> Value; internal Pending? Next; }
    private readonly Lock _lock = new();
    private int _count;
    private Entry[] _entries;
    private Pending? _pending;

    public Cache(int capacity = 2) => _entries = new Entry[capacity <= 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)capacity)];

    public Cache(IEnumerable<KeyValuePair<K, V>> items, int capacity = 2) : this(capacity)
    {
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
        _count = entries[bucketIndex].Bucket = _count + 1;
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
        return _entries = newEntries;
    }

    public ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory) => GetOrAdd(key, factory, static (k, f) => f(k));

    public ValueTask<V> GetOrAdd<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        var hashCode = key.GetHashCode();
        var count = _count;
        var entries = Volatile.Read(ref _entries);
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            if (key.Equals(entries[i].Key)) return new(entries[i].Value);
            i = entries[i].Next;
        }
        lock (_lock)
        {
            if (_count != count)
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

    public Task<V> Update(K key, Func<K, Task<V>> factory) => Update(key, factory, static (k, f) => f(k));

    public Task<V> Update<TState>(K key, TState state, Func<K, TState, Task<V>> factory)
    {
        lock (_lock)
            return GetOrAddPending(key, state, factory).Value;
    }

    public async Task<Cache<K, V>> Compact(Func<KeyValuePair<K, V>, bool> keep)
    {
        while (true)
        {
            var pending = _pending;
            while (pending is not null)
            {
                try { await pending.Value; }
                catch { }
                pending = pending.Next;
            }
            var newCount = this.Count(keep);
            var count = _count;
            if (count == newCount) return this;
            var newCache = new Cache<K, V>(this.Where(keep), newCount);
            if (_count == count && _pending is null) return newCache;
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

/// <summary><para>
/// With <paramref name="duration"/> = 1 min and <paramref name="eagerRefreshRatio"/> = 0.5:
/// <list type="bullet">
///     <item>Any access after 30 sec triggers a background refresh (caller gets the current value immediately).</item>
///     <item>Any access after 1 min waits for the refresh possibly with a <paramref name="softTimeout"/> to return the current value if it's taking too long.</item>
///     <item>Popular items get accessed in the 30 sec – 1 min window, so they refresh eagerly at 30 sec. Unpopular items are never accessed in that window, so they only refresh after the <paramref name="duration"/> (1 min).</item>
///     <item>If the factory errors or takes too long the current item is returned as a failsafe. Items are removed after the given <paramref name="remove"/> time since last refresh.</item>
/// </list>
/// </para>
/// <para>The core insight is: popularity correlates with the value of freshness. Here are concrete use cases:</para>
/// <list type="number">
///     <item>Stock/pricing tickers — AAPL is viewed by thousands of users per second; a stale price is seen by many people and noticed quickly. A penny stock viewed once an hour can tolerate twice the staleness because almost nobody sees it.</item>
///     <item>E-commerce inventory/pricing — A trending product's stock count matters enormously (overselling risk scales with traffic). A long-tail product with 2 views/day can show a slightly older stock count with negligible business impact.</item>
///     <item>CDN origin-shield / API gateway caching — High-traffic API endpoints (e.g. homepage feed) benefit from proactive refresh so no user ever sees a cache-miss latency spike. Low-traffic endpoints (e.g. an obscure settings page) can tolerate the occasional blocking refresh because few users are affected.</item>
///     <item>DNS / service discovery — A hot service endpoint that receives 10k req/s should detect failovers and IP changes faster. A rarely-called internal tool can lag behind without anyone noticing.</item>
///     <item>Permissions / feature-flag caching — An active user's permissions are checked constantly; fresher data means revocations or new grants take effect sooner. A dormant user's cached permissions can be staler since they're rarely evaluated.</item>
///     <item>Social metrics (likes, comments, share counts) — A viral post's counters are seen by millions; keeping them fresher is worth the extra backend call. An old post with 3 views/day doesn't need that.</item>
/// </list>
/// <para>
/// The unifying principle: the cost of staleness is proportional to the number of people who observe it. Eager refresh naturally directs your refresh budget toward the items
/// where staleness hurts the most, without any explicit popularity tracking — the access pattern itself is the signal. This is a very efficient way to implement a refreshing cache.
/// </para></summary>
public sealed class RefreshingCache<K, V> : IDisposable where K : IEquatable<K>
{
    private Cache<K, (long Timestamp, V Value)> _cache = new();
    private readonly long _durationTicks, _eagerRefreshTicks;
    private readonly TimeSpan _softTimeout;
    private readonly Timer? _timer;

    public RefreshingCache(TimeSpan duration, double eagerRefreshRatio = 0.5, TimeSpan? softTimeout = null, TimeSpan? remove = null)
    {
        _durationTicks = (long)(duration.TotalSeconds * Stopwatch.Frequency);
        _eagerRefreshTicks = (long)(_durationTicks * Math.Clamp(eagerRefreshRatio, 0.0, 1.0));
        _softTimeout = softTimeout ?? TimeSpan.Zero;
        if (remove is { } r) _timer = new Timer(Cleanup, (long)(r.TotalSeconds * Stopwatch.Frequency), r * 1.25, r * 0.25);
    }

    private void Cleanup(object? removeTicks)
    {
        _ = Task.Run(async () =>
        {
            var removeTimestamp = Stopwatch.GetTimestamp() - (long)removeTicks!;
            _cache = await _cache.Compact(e => e.Value.Timestamp >= removeTimestamp);
        });
    }

    private static async Task<(long, V)> CallFactory(K key, Func<K, Task<V>> factory)
    {
        var value = await factory(key);
        return (Stopwatch.GetTimestamp(), value);
    }

    public async ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
    {
        var now = Stopwatch.GetTimestamp();
        var result = await _cache.GetOrAdd(key, factory, CallFactory);
        var age = now - result.Timestamp;
        if (age >= _eagerRefreshTicks)
        {
            var updateTask = _cache.Update(key, factory, CallFactory);
            if (age >= _durationTicks && (_softTimeout == TimeSpan.Zero || await Task.WhenAny(updateTask, Task.Delay(_softTimeout)) == updateTask))
                try { result = await updateTask; } catch { }
            else
                _ = updateTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        return result.Value;
    }

    public void Dispose() => _timer?.Dispose();
}