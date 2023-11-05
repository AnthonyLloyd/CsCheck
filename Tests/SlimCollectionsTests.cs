namespace Tests;

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CsCheck;
using Xunit;

public class SlimCollectionsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    readonly Action<string> writeLine = output.WriteLine;

    [Fact]
    public void ListSlim_ModelBased()
    {
        Gen.Int.Array.Select(a => (new ListSlim<int>(a), new List<int>(a)))
        .SampleModelBased(
            Gen.Int.Operation<ListSlim<int>, List<int>>((ls, l, i) =>
            {
                ls.Add(i);
                l.Add(i);
            })
        );
    }

    [Fact]
    public void ListSlim_Concurrency()
    {
        Gen.Byte.Array.Select(a => new ListSlim<byte>(a))
        .SampleConcurrent(
            Gen.Byte.Operation<ListSlim<byte>>((l, i) => { lock (l) l.Add(i); }),
            Gen.Int.NonNegative.Operation<ListSlim<byte>>((l, i) => { if (i < l.Count) { var _ = l[i]; } }),
            Gen.Int.NonNegative.Select(Gen.Byte).Operation<ListSlim<byte>>((l, t) => { if (t.Item1 < l.Count) l[t.Item1] = t.Item2; }),
            Gen.Operation<ListSlim<byte>>(l => l.ToArray())
        );
    }

    [Fact]
    public void SetSlim_ModelBased()
    {
        Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
        .SampleModelBased(
            Gen.Int.Operation<SetSlim<int>, HashSet<int>>((ls, l, i) =>
            {
                ls.Add(i);
                l.Add(i);
            })
        );
    }

    [Fact]
    public void SetSlim_Concurrency()
    {
        Gen.Byte.Array.Select(a => new SetSlim<byte>(a))
        .SampleConcurrent(
            Gen.Byte.Operation<SetSlim<byte>>((l, i) => { lock (l) l.Add(i); }),
            Gen.Int.NonNegative.Operation<SetSlim<byte>>((l, i) => { if (i < l.Count) { var _ = l[i]; } }),
            Gen.Byte.Operation<SetSlim<byte>>((l, i) => { var _ = l.IndexOf(i); }),
            Gen.Operation<SetSlim<byte>>(l => { var _ = l.ToArray(); })
        );
    }

    [Fact]
    public void SetSlim_Performance_Add()
    {
        Gen.Int.Array
        .Faster(
            a =>
            {
                var s = new SetSlim<int>();
                foreach (var i in a) s.Add(i);
            },
            a =>
            {
                var s = new HashSet<int>();
                foreach (var i in a) s.Add(i);
            },
            repeat: 100, raiseexception: false
        ).Output(writeLine);
    }

    [Fact(Skip = "fails")]
    public void SetSlim_Performance_Contains()
    {
        Gen.Int.Array.Select(a => (a, new SetSlim<int>(a), new HashSet<int>(a)))
        .Faster(
            (items, setslim, _) =>
            {
                foreach (var i in items) setslim.Contains(i);
            },
            (items, _, hashset) =>
            {
                foreach (var i in items) hashset.Contains(i);
            },
            repeat: 1000
        ).Output(writeLine);
    }

    [Fact]
    public void MapSlim_ModelBased()
    {
        Gen.Dictionary(Gen.Int, Gen.Byte)
        .Select(d => (new MapSlim<int, byte>(d), new Dictionary<int, byte>(d)))
        .SampleModelBased(
            Gen.Select(Gen.Int[0, 100], Gen.Byte).Operation<MapSlim<int, byte>, Dictionary<int, byte>>((m, d, t) =>
            {
                m[t.Item1] = t.Item2;
                d[t.Item1] = t.Item2;
            })
        );
    }

    [Fact]
    public void MapSlim_Metamorphic()
    {
        Gen.Dictionary(Gen.Int, Gen.Byte).Select(d => new MapSlim<int, byte>(d))
        .SampleMetamorphic(
            Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
                (d, t) => { d[t.Item1] = t.Item2; d[t.Item3] = t.Item4; },
                (d, t) => { if (t.Item1 == t.Item3) { d[t.Item3] = t.Item4; } else { d[t.Item3] = t.Item4; d[t.Item1] = t.Item2; } }
            )
        );
    }

    [Fact]
    public void MapSlim_Concurrency()
    {
        Gen.Dictionary(Gen.Int, Gen.Byte).Select(d => new MapSlim<int, byte>(d))
        .SampleConcurrent(
            Gen.Int.Select(Gen.Byte).Operation<MapSlim<int, byte>>((m, t) => { lock (m) m[t.Item1] = t.Item2; }),
            Gen.Int.NonNegative.Operation<MapSlim<int, byte>>((m, i) => { if (i < m.Count) { var _ = m.Key(i); } }),
            Gen.Int.Operation<MapSlim<int, byte>>((m, i) => { var _ = m.IndexOf(i); }),
            Gen.Operation<MapSlim<int, byte>>(m => { var _ = m.ToArray(); })
        );
    }

    [Fact]
    public void MapSlim_Performance_Add()
    {
        Gen.Int.Select(Gen.Byte).Array
        .Faster(
            items =>
            {
                var m = new MapSlim<int, byte>();
                foreach (var (k, v) in items) m[k] = v;
            },
            items =>
            {
                var m = new Dictionary<int, byte>();
                foreach (var (k, v) in items) m[k] = v;
            },
            repeat: 100, raiseexception: false
        ).Output(writeLine);
    }

    [Fact(Skip = "fails")]
    public void MapSlim_Performance_IndexOf()
    {
        Gen.Dictionary(Gen.Int, Gen.Byte)
        .Select(a => (a, new MapSlim<int, byte>(a), new Dictionary<int, byte>(a)))
        .Faster(
            (items, mapslim, _) =>
            {
                foreach (var (k, _) in items) mapslim.IndexOf(k);
            },
            (items, _, dict) =>
            {
                foreach (var (k, _) in items) dict.ContainsKey(k);
            },
            repeat: 100
        ).Output(writeLine);
    }

    [Fact(Skip = "fails")]
    public void MapSlim_Performance_Increment()
    {
        Gen.Int[0, 255].Array
        .Select(a => (a, new MapSlim<int, int>(), new Dictionary<int, int>()))
        .Faster(
            (items, mapslim, _) =>
            {
                foreach (var b in items)
                    mapslim.GetValueOrNullRef(b)++;
            },
            (items, _, dict) =>
            {
                foreach (var b in items)
                {
                    dict.TryGetValue(b, out int c);
                    dict[b] = c + 1;
                }
            },
            repeat: 1000, sigma: 10
        ).Output(writeLine);
    }
}

public class ListSlim<T> : IReadOnlyList<T>
{
    static class Holder { internal static T[] Initial = []; }
    T[] entries;
    int count;
    public ListSlim() => entries = Holder.Initial;
    public ListSlim(int capacity) => entries = new T[capacity];
    public ListSlim(IEnumerable<T> items)
    {
        if (items is ICollection<T> ts)
        {
            entries = new T[ts.Count];
            ts.CopyTo(entries, 0);
        }
        else
        {
            entries = items.ToArray();
        }

        count = entries.Length;
    }
    public int Count => count;
    public T this[int i]
    {
        get => entries[i];
        set => entries[i] = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void AddWithResize(T item)
    {
        if (count == 0)
        {
            entries = new T[2];
            entries[0] = item;
            count = 1;
        }
        else
        {
            var newEntries = new T[count * 2];
            Array.Copy(entries, 0, newEntries, 0, count);
            newEntries[count] = item;
            entries = newEntries;
            count++;
        }
    }

    public void Add(T item)
    {
        T[] e = entries;
        int c = count;
        if ((uint)c < (uint)e.Length)
        {
            e[c] = item;
            count = c + 1;
        }
        else
        {
            AddWithResize(item);
        }
    }

    public T[] ToArray()
    {
        int c = count;
        var a = new T[c];
        Array.Copy(entries, 0, a, 0, c);
        return a;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < count; i++)
            yield return entries[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class SetSlim<T> : IReadOnlyCollection<T> where T : IEquatable<T>
{
    struct Entry { internal int Bucket; internal int Next; internal T Item; }
    static class Holder { internal static Entry[] Initial = new Entry[1]; }
    int count;
    Entry[] entries;
    public SetSlim() => entries = Holder.Initial;

    public SetSlim(int capacity)
    {
        if (capacity < 2) capacity = 2;
        entries = new Entry[PowerOf2(capacity)];
    }

    public SetSlim(IEnumerable<T> items)
    {
        entries = new Entry[2];
        foreach (var i in items) Add(i);
    }

    static int PowerOf2(int capacity)
    {
        if ((capacity & (capacity - 1)) == 0) return capacity;
        int i = 2;
        while (i < capacity) i <<= 1;
        return i;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    Entry[] Resize()
    {
        if (entries.Length == 1) return entries = new Entry[2];
        var oldEntries = entries;
        var newEntries = new Entry[oldEntries.Length * 2];
        for (int i = 0; i < oldEntries.Length;)
        {
            var bucketIndex = oldEntries[i].Item.GetHashCode() & (newEntries.Length - 1);
            newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
            newEntries[i].Item = oldEntries[i].Item;
            newEntries[bucketIndex].Bucket = ++i;
        }
        return entries = newEntries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    int AddItem(T item, int hashCode)
    {
        var i = count;
        var ent = entries;
        if (ent.Length == i || ent.Length == 1) ent = Resize();
        var bucketIndex = hashCode & (ent.Length - 1);
        ent[i].Next = ent[bucketIndex].Bucket - 1;
        ent[i].Item = item;
        ent[bucketIndex].Bucket = ++count;
        return i;
    }

    public int Add(T item)
    {
        var ent = entries;
        var hashCode = item.GetHashCode();
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0 && !item.Equals(ent[i].Item)) i = ent[i].Next;
        return i >= 0 ? i : AddItem(item, hashCode);
    }

    public int IndexOf(T item)
    {
        var ent = entries;
        var hashCode = item.GetHashCode();
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0 && !item.Equals(ent[i].Item)) i = ent[i].Next;
        return i;
    }

    public bool Contains(T item)
    {
        var ent = entries;
        var hashCode = item.GetHashCode();
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0 && !item.Equals(ent[i].Item)) i = ent[i].Next;
        return i >= 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < count; i++)
            yield return entries[i].Item;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public T this[int i] => entries[i].Item;
    public int Count => count;
}

public class MapSlim<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
{
    struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
    static class Holder { internal static Entry[] Initial = new Entry[1]; }
    int count;
    Entry[] entries;
    public MapSlim() => entries = Holder.Initial;

    public MapSlim(int capacity)
    {
        if (capacity < 2) capacity = 2;
        entries = new Entry[PowerOf2(capacity)];
    }

    public MapSlim(IEnumerable<KeyValuePair<K, V>> items)
    {
        entries = new Entry[2];
        foreach (var (k, v) in items) this[k] = v;
    }

    public int Count => count;

    static int PowerOf2(int capacity)
    {
        if ((capacity & (capacity - 1)) == 0) return capacity;
        int i = 2;
        while (i < capacity) i <<= 1;
        return i;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    Entry[] Resize()
    {
        var oldEntries = entries;
        if (oldEntries.Length == 1) return entries = new Entry[2];
        var newEntries = new Entry[oldEntries.Length * 2];
        for (int i = 0; i < oldEntries.Length;)
        {
            var bucketIndex = oldEntries[i].Key.GetHashCode() & (newEntries.Length - 1);
            newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
            newEntries[i].Key = oldEntries[i].Key;
            newEntries[i].Value = oldEntries[i].Value;
            newEntries[bucketIndex].Bucket = ++i;
        }
        return entries = newEntries;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void AddItem(K key, V value, int hashCode)
    {
        var i = count;
        var ent = entries;
        if (ent.Length == i || ent.Length == 1) ent = Resize();
        var bucketIndex = hashCode & (ent.Length - 1);
        ent[i].Next = ent[bucketIndex].Bucket - 1;
        ent[i].Key = key;
        ent[i].Value = value;
        ent[bucketIndex].Bucket = ++count;
    }

    public V this[K key]
    {
        get
        {
            var ent = entries;
            var hashCode = key.GetHashCode();
            var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
            while (i >= 0 && !key.Equals(ent[i].Key)) i = ent[i].Next;
            return ent[i].Value;
        }
        set
        {
            var ent = entries;
            var hashCode = key.GetHashCode();
            var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
            while (i >= 0 && !key.Equals(ent[i].Key)) i = ent[i].Next;
            if (i >= 0) ent[i].Value = value;
            else AddItem(key, value, hashCode);
        }
    }

    public ref V GetValueOrNullRef(K key)
    {
        var ent = entries;
        var hashCode = key.GetHashCode();
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0 && !key.Equals(ent[i].Key)) i = ent[i].Next;
        if (i >= 0)
        {
            return ref ent[i].Value;
        }
        else
        {
            i = count;
            if (ent.Length == i || ent.Length == 1) ent = Resize();
            var bucketIndex = hashCode & (ent.Length - 1);
            ent[i].Next = ent[bucketIndex].Bucket - 1;
            ent[i].Key = key;
            ent[i].Value = default;
            ent[bucketIndex].Bucket = ++count;
            return ref ent[i].Value;
        }
    }

    public int IndexOf(K key)
    {
        var ent = entries;
        var hashCode = key.GetHashCode();
        var i = ent[hashCode & (ent.Length - 1)].Bucket - 1;
        while (i >= 0 && !key.Equals(ent[i].Key)) i = ent[i].Next;
        return i;
    }

    public K Key(int i) => entries[i].Key;
    public V Value(int i) => entries[i].Value;

    public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
    {
        for (int i = 0; i < count; i++)
            yield return new(entries[i].Key, entries[i].Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}