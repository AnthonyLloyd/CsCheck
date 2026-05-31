namespace Tests;

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary>A thread-safe cache with lock-free reads and stampede-free population.
/// Concurrent requests for the same key share a single pending task, avoiding redundant factory calls.
/// Uses power-of-two sized buckets for efficient bitwise index calculation and cache-friendly array storage.</summary>
public sealed class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    private sealed class Pending { internal required K Key; internal required Task<V> Value; internal volatile Pending? Next; }
    private volatile int _count;
    private volatile Entry[] _entries;
    private volatile Pending? _pending;
    private readonly Lock _lock = new();

    public Cache(int capacity = 2) => _entries = new Entry[capacity <= 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)capacity)];

    public Cache(IEnumerable<KeyValuePair<K, V>> items, int capacity = 2) : this(capacity)
    {
        foreach (var (k, v) in items) AddOrUpdate(k, v);
    }

    public int Count => _count;
    public bool IsEmpty => _pending is null && _count == 0;

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
        var entries = _entries;
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            ref var e = ref entries[i];
            if (key.Equals(e.Key)) return new(e.Value);
            i = e.Next;
        }
        lock (_lock)
        {
            entries = _entries;
            i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
            while (i >= 0)
            {
                ref var e = ref entries[i];
                if (key.Equals(e.Key)) return new(e.Value);
                i = e.Next;
            }
            return new(GetOrAddPending(key, state, factory).Value);
        }
    }

    public bool TryGetValue(K key, out V value)
    {
        var hashCode = key.GetHashCode();
        var entries = _entries;
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            ref var e = ref entries[i];
            if (key.Equals(e.Key)) { value = e.Value; return true; }
            i = e.Next;
        }
        value = default!;
        return false;
    }

    public Task<V>? TryGetPending(K key)
    {
        var pending = _pending;
        while (pending is not null)
        {
            if (key.Equals(pending.Key)) return pending.Value;
            pending = pending.Next;
        }
        return null;
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
                        AddOrUpdate(key, value);
                        RemovePending(pending);
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

/// <summary>Lightweight cache with refresh-ahead, single-flight, Dispose free, and fail-safe semantics. Features match FusionCache and similar to web <see href="https://www.debugbear.com/docs/stale-while-revalidate">stale-while-revalidate</see>.</summary>
/// <param name="duration">How long a value stays fresh. After this, the next access waits for a refresh.</param>
/// <param name="eagerRefreshRatio">Fraction of <paramref name="duration"/> after which an access triggers a background refresh while still returning the current value.</param>
/// <param name="softTimeout">If a synchronous refresh takes longer than this, return the current (stale) value while the refresh continues in the background.</param>
/// <param name="remove">Items not refreshed within this duration become eligible for removal and are removed by periodic cleanup.</param>
/// <remarks>
/// <para>With <paramref name="duration"/> = 1 min and <paramref name="eagerRefreshRatio"/> = 0.5:
/// <list type="bullet">
///     <item>Any access after 30 sec triggers a background refresh (caller gets the current value immediately).</item>
///     <item>Any access after 1 min waits for the refresh, possibly with a <paramref name="softTimeout"/> to return the current value if it's taking too long.</item>
///     <item>Popular items get accessed in the 30 sec – 1 min window, so they refresh eagerly at 30 sec. Unpopular items are never accessed in that window, so they only refresh after the <paramref name="duration"/> (1 min).</item>
///     <item>If the factory errors or takes too long the current item is returned as a failsafe. Items are removed after the given <paramref name="remove"/> time since last refresh.</item>
/// </list></para>
/// <para>The core insight: popularity correlates with the value of freshness. Eager refresh naturally directs the refresh budget toward the items where staleness hurts the most,
/// without any explicit popularity tracking, the access pattern itself is the signal.</para>
/// </remarks>
public sealed class RefreshingCache<K, V>(TimeSpan duration, double eagerRefreshRatio = 0.5, TimeSpan? softTimeout = null, TimeSpan? remove = null) where K : IEquatable<K>
{
    private volatile Cache<K, (long Timestamp, V Value)> _cache = new();
    private volatile Timer? _timer;
    private readonly long _durationMs = (long)duration.TotalMilliseconds;
    private readonly long _eagerRefreshMs = (long)(duration.TotalMilliseconds * Math.Clamp(eagerRefreshRatio, 0.0, 1.0));
    private readonly TimeSpan _softTimeout = softTimeout ?? Timeout.InfiniteTimeSpan;
    private readonly long _removeMs = remove.HasValue ? (long)remove.Value.TotalMilliseconds : 0;

    private static void Cleanup(object? state)
    {
        if (((WeakReference<RefreshingCache<K, V>>)state!).TryGetTarget(out var self))
            _ = Task.Run(async () =>
            {
                var removeTimestamp = Environment.TickCount64 - self._removeMs;
                self._cache = await self._cache.Compact(e => e.Value.Timestamp >= removeTimestamp);
                if (self._cache.IsEmpty)
                {
                    Interlocked.Exchange(ref self._timer, null)?.Dispose();
                    if (!self._cache.IsEmpty) self.EnsureTimerRunning();
                }
                else
                    self._timer!.Change(self._removeMs, Timeout.Infinite);
            });
    }

    private void EnsureTimerRunning()
    {
        if (_removeMs == 0 || _timer is not null) return;
        var newTimer = new Timer(Cleanup, new WeakReference<RefreshingCache<K, V>>(this), Timeout.Infinite, Timeout.Infinite);
        if (Interlocked.CompareExchange(ref _timer, newTimer, null) is null)
            newTimer.Change(_removeMs, Timeout.Infinite);
        else newTimer.Dispose();
    }

    private static async Task<(long, V)> CallFactory<S>(K key, (Func<K, S, Task<V>> Factory, S State, RefreshingCache<K, V> Self) state)
    {
        var value = await state.Factory(key, state.State);
        var timestamp = Environment.TickCount64;
        state.Self.EnsureTimerRunning();
        return (timestamp, value);
    }

    public ValueTask<V> GetOrAdd<S>(K key, S state, Func<K, S, Task<V>> factory)
    {
        if (_cache.TryGetValue(key, out var result))
        {
            var age = Environment.TickCount64 - result.Timestamp;
            if (age < _eagerRefreshMs) return new(result.Value);
            var updateTask = _cache.TryGetPending(key) ?? _cache.Update(key, (factory, state, this), CallFactory);
            if (age < _durationMs)
            {
                _ = updateTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return new(result.Value);
            }
            return GetOrAddRefresh(updateTask, result);
        }
        return GetOrAddAwait(_cache.GetOrAdd(key, (factory, state, this), CallFactory));
    }

    private static async ValueTask<V> GetOrAddAwait(ValueTask<(long Timestamp, V Value)> task) => (await task).Value;

    private async ValueTask<V> GetOrAddRefresh(Task<(long Timestamp, V Value)> updateTask, (long Timestamp, V Value) result)
    {
        try
        {
            result = _softTimeout == Timeout.InfiniteTimeSpan ? await updateTask : await updateTask.WithDefault(result, _softTimeout);
        }
        catch { }
        return result.Value;
    }

    public ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
        => GetOrAdd(key, factory, static (k, f) => f(k));

    public async ValueTask<V> Update<S>(K key, S state, Func<K, S, Task<V>> factory)
        => (await _cache.Update(key, (factory, state, this), CallFactory)).Value;

    public ValueTask<V> Update(K key, Func<K, Task<V>> factory)
        => Update(key, factory, static (k, f) => f(k));
}

public static class TaskExtensions
{
    public static Task<T> WithDefault<T>(this Task<T> task, T defaultValue, TimeSpan timeout)
    {
        if (task.IsCompletedSuccessfully) return task;
        if (task.IsCompleted)
        {
            if (task.IsFaulted) _ = task.Exception;
            return Task.FromResult(defaultValue);
        }
        return WithDefaultTask(task, defaultValue, timeout);
    }

    public static ValueTask<T> WithDefault<T>(this ValueTask<T> task, T defaultValue, TimeSpan timeout)
    {
        if (task.IsCompletedSuccessfully) return task;
        if (task.IsCompleted)
        {
            if (task.IsFaulted) _ = task.AsTask().Exception;
            return new(defaultValue);
        }
        return new(WithDefaultTask(task.AsTask(), defaultValue, timeout));
    }

    private static Task<T> WithDefaultTask<T>(Task<T> task, T defaultValue, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new Timer(static s =>
        {
            var (tcs, defaultValue) = ((TaskCompletionSource<T>, T))s!;
            tcs.TrySetResult(defaultValue);
        }, (tcs, defaultValue), timeout, Timeout.InfiniteTimeSpan);
        task.ContinueWith(static (t, s) =>
        {
            var (tcs, timer, defaultValue) = ((TaskCompletionSource<T>, Timer, T))s!;
            timer.Dispose();
            if (t.IsCompletedSuccessfully) tcs.TrySetResult(t.Result);
            else { tcs.TrySetResult(defaultValue); _ = t.Exception; }
        }, (tcs, timer, defaultValue), TaskContinuationOptions.ExecuteSynchronously);
        return tcs.Task;
    }
}
