namespace Tests;

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

// Remove where a predicate is true for expiry.
// Kick off a pending for refresh.

public class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    private int _count;
    private Entry[] _entries;
    private Pending? _pending;
    private readonly Lock _entriesLock = new();
    private readonly Lock _pendingLock = new();

    public Cache() => _entries = new Entry[2];
    public Cache(int capacity) => _entries = new Entry[capacity < 2 ? 2 : BitOperations.RoundUpToPowerOf2((uint)capacity)];

    public Cache(IEnumerable<KeyValuePair<K, V>> items)
    {
        _entries = new Entry[2];
        foreach (var (k, v) in items) this[k] = v;
    }

    public int Count => _count;

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

    public V this[K key]
    {
        set
        {
            var hashCode = key.GetHashCode();
            lock (_entriesLock)
            {
                var ent = _entries;
                var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
                while (i >= 0 && !key.Equals(ent[i].Key)) i = ent[i].Next;
                if (i >= 0)
                {
                    ent[i].Value = value;
                    return;
                }
                i = _count;
                if (ent.Length == i) ent = Resize();
                var bucketIndex = hashCode & (ent.Length - 1);
                ent[i].Next = ent[bucketIndex].Bucket - 1;
                ent[i].Key = key;
                ent[i].Value = value;
                ent[bucketIndex].Bucket = ++_count;
            }
        }
    }

    public ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
    {
        var hashCode = key.GetHashCode();
        var count = _count;
        var ent = _entries;
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0)
        {
            if (key.Equals(ent[i].Key)) return new(ent[i].Value);
            i = ent[i].Next;
        }
        return AddPending(key, factory, hashCode, count);
    }

    private ValueTask<V> AddPending(K key, Func<K, Task<V>> factory, int hashCode, int count)
    {
        lock (_pendingLock)
        {
            if (_count != count)
            {
                var ent = _entries;
                var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
                while (i >= 0)
                {
                    if (key.Equals(ent[i].Key)) return new(ent[i].Value);
                    i = ent[i].Next;
                }
            }
            var pending = _pending;
            if (pending is null)
                return new((_pending = new(key)).Start(factory, this));
            while (true)
            {
                if (key.Equals(pending.Key)) return new(pending.Value);
                if (pending.Next is null)
                    return new((pending.Next = new(key)).Start(factory, this));
                pending = pending.Next;
            }
        }
    }

    private void RemovePending(Pending remove)
    {
        lock (_pendingLock)
        {
            var pending = _pending;
            if (ReferenceEquals(pending, remove))
            {
                _pending = null;
            }
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
        public readonly K Key;
        internal Task<V> Value;
        public Pending? Next;

        internal Pending(K key)
        {
            Key = key;
            Value = null!;
        }

        internal Task<V> Start(Func<K, Task<V>> factory, Cache<K, V> cache) =>
            Value = Task.Run(async () =>
                    {
                        try
                        {
                            var value = await factory(Key);
                            cache[Key] = value;
                            return value;
                        }
                        finally
                        {
                            cache.RemovePending(this);
                        }
                    });
    }

    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return new(_entries[i].Key, _entries[i].Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
