namespace Tests;

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

public class Cache<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    private struct Entry { internal int Bucket; internal int Next; internal uint HashCode; internal K Key; internal V Value; }
    private sealed class Pending { internal required K Key; internal required Task<V> Value; internal Pending? Next; }
    private int _count;
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
        var hashCode = (uint)key.GetHashCode();
        var entries = _entries;
        var mask = entries.Length - 1;
        var i = entries[(int)(hashCode & mask)].Bucket - 1;
        while (i >= 0 && !key.Equals(entries[i].Key)) i = entries[i].Next;
        if (i >= 0)
        {
            entries[i].Value = value;
            return;
        }
        i = _count;
        if (entries.Length == i)
        {
            entries = Resize();
            mask = entries.Length - 1;
        }
        var bucketIndex = (int)(hashCode & mask);
        entries[i].HashCode = hashCode;
        entries[i].Next = entries[bucketIndex].Bucket - 1;
        entries[i].Key = key;
        entries[i].Value = value;
        entries[bucketIndex].Bucket = ++_count;
        _entries = entries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Entry[] Resize()
    {
        // Resize is only called when _count == oldEntries.Length (array is full)
        // so all entries from [0, oldEntries.Length) have valid HashCode values
        var oldEntries = _entries;
        var newEntries = new Entry[oldEntries.Length * 2];
        var newMask = newEntries.Length - 1;
        for (int i = 0; i < oldEntries.Length;)
        {
            var hashCode = oldEntries[i].HashCode;
            var bucketIndex = (int)(hashCode & newMask);
            newEntries[i].HashCode = hashCode;
            newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
            newEntries[i].Key = oldEntries[i].Key;
            newEntries[i].Value = oldEntries[i].Value;
            newEntries[bucketIndex].Bucket = ++i;
        }
        return newEntries;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<V> GetOrAdd(K key, Func<K, Task<V>> factory)
    {
        var count = _count;
        var entries = _entries;
        var hashCode = (uint)key.GetHashCode();
        var mask = entries.Length - 1;
        var i = entries[(int)(hashCode & mask)].Bucket - 1;
        while (i >= 0)
        {
            if (key.Equals(entries[i].Key)) return new(entries[i].Value);
            i = entries[i].Next;
        }
        lock (_entries)
        {
            if (_count != count)
            {
                entries = _entries;
                mask = entries.Length - 1;
                i = entries[(int)(hashCode & mask)].Bucket - 1;
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
        lock (_entries)
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
            return _pending = CreatePending(key, factory);
        while (true)
        {
            if (key.Equals(pending.Key)) return pending;
            if (pending.Next is null)
                return pending.Next = CreatePending(key, factory);
            pending = pending.Next;
        }
    }

    private Pending CreatePending(K key, Func<K, Task<V>> factory)
    {
        Pending pending = null!;
        pending = new Pending
        {
            Key = key,
            Value = Task.Run(async () =>
            {
                try
                {
                    var value = await factory(key);
                    lock (_entries)
                    {
                        RemovePending(pending);
                        AddOrUpdate(key, value);
                    }
                    return value;
                }
                catch
                {
                    lock (_entries)
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
