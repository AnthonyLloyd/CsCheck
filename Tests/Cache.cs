namespace Tests;

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

public class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    private int _count;
    private Entry[] _entries;
    private Pending? _pending;
    private readonly Lock _pendingLock = new();

    public Cache() => _entries = new Entry[2];
    public Cache(int capacity) => _entries = new Entry[capacity < 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)capacity)];

    public Cache(IEnumerable<KeyValuePair<K, V>> items)
    {
        _entries = new Entry[2];
        foreach (var (k, v) in items) this[k] = v;
    }

    public int Count => _count;

    public V this[K key]
    {
        set
        {
            var hashCode = key.GetHashCode();
            lock (_entries)
            {
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
                _entries = entries;
            }
        }
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
    {
        var count = _count;
        var entries = _entries;
        var hashCode = key.GetHashCode();
        var i = entries[hashCode & (entries.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            if (key.Equals(entries[i].Key)) return new(entries[i].Value);
            i = entries[i].Next;
        }
        lock (_pendingLock)
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
            return new(GetOrAddPending(key, factory).Value);
        }
    }

    public Task<V> Update(K key, Func<K, Task<V>> factory)
    {
        lock (_pendingLock)
            return GetOrAddPending(key, factory).Value;
    }

    public async ValueTask WaitPending()
    {
        var pending = _pending;
        while (pending is not null)
        {
            try { _ = await pending.Value; } catch { }
            pending = pending.Next;
        }
    }

    private Pending GetOrAddPending(K key, Func<K, Task<V>> factory)
    {
        var pending = _pending;
        if (pending is null)
            return _pending = new Pending(this, key, factory);
        while (true)
        {
            if (key.Equals(pending.Key)) return pending;
            if (pending.Next is null)
                return pending.Next = new Pending(this, key, factory);
            pending = pending.Next;
        }
    }

    private void RemovePending(Pending remove)
    {
        lock (_pendingLock)
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
    }

    private sealed class Pending
    {
        internal readonly K Key;
        internal readonly Task<V> Value;
        internal Pending? Next;

        internal Pending(Cache<K, V> cache, K key, Func<K, Task<V>> factory)
        {
            Key = key;
            Value = Task.Run(async () =>
            {
                try
                {
                    return cache[key] = await factory(key);
                }
                finally
                {
                    cache.RemovePending(this);
                }
            });
        }
    }

    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return new(_entries[i].Key, _entries[i].Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
