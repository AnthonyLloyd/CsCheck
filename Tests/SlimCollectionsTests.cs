﻿using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CsCheck;
using Xunit;

namespace Tests
{
    public class SlimCollectionsTests
    {
        readonly Action<string> writeLine;
        public SlimCollectionsTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;
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
                Gen.Int.NonNegative.Select(Gen.Byte).Operation<ListSlim<byte>>((l, t) => { if (t.V0 < l.Count) l[t.V0] = t.V1; }),
                Gen.Operation<ListSlim<byte>>(l => l.ToArray())
            );
        }

        [Fact]
        public void ListSlim_Performance_Add()
        {
            Gen.Int.Array
            .Faster(
                t =>
                {
                    var d = new ListSlim<int>();
                    for (int i = 0; i < t.Length; i++)
                        d.Add(t[i]);
                    return d.Count;
                },
                t =>
                {
                    var d = new List<int>();
                    for (int i = 0; i < t.Length; i++)
                        d.Add(t[i]);
                    return d.Count;
                },
                repeat: 500, sigma: 20
            ).Output(writeLine);
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
                repeat: 500, sigma: 20
            ).Output(writeLine);
        }

        [Fact]
        public void SetSlim_Performance_Contains()
        {
            Gen.Int.Array
            .Select(a => (a, new SetSlim<int>(a), new HashSet<int>(a)))
            .Faster(
                t =>
                {
                    var s = t.Item2;
                    foreach (var i in t.a) s.Contains(i);
                },
                t =>
                {
                    var s = t.Item3;
                    foreach (var i in t.a) s.Contains(i);
                },
                repeat: 100
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
                    m[t.V0] = t.V1;
                    d[t.V0] = t.V1;
                })
            );
        }

        [Fact]
        public void MapSlim_Metamorphic()
        {
            Gen.Dictionary(Gen.Int, Gen.Byte).Select(d => new MapSlim<int, byte>(d))
            .SampleMetamorphic(
                Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
                    (d, t) => { d[t.V0] = t.V1; d[t.V2] = t.V3; },
                    (d, t) => { if (t.V0 == t.V2) d[t.V2] = t.V3; else { d[t.V2] = t.V3; d[t.V0] = t.V1; } }
                )
            );
        }

        [Fact]
        public void MapSlim_Concurrency()
        {
            Gen.Dictionary(Gen.Int, Gen.Byte).Select(d => new MapSlim<int, byte>(d))
            .SampleConcurrent(
                Gen.Int.Select(Gen.Byte).Operation<MapSlim<int, byte>>((m, t) => { lock (m) m[t.V0] = t.V1; }),
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
                a =>
                {
                    var m = new MapSlim<int, byte>();
                    foreach (var (k, v) in a) m[k] = v;
                },
                a =>
                {
                    var m = new Dictionary<int, byte>();
                    foreach (var (k, v) in a) m[k] = v;
                },
                repeat: 100
            ).Output(writeLine);
        }

        [Fact]
        public void MapSlim_Performance_IndexOf()
        {
            Gen.Dictionary(Gen.Int, Gen.Byte)
            .Select(a => (a, new MapSlim<int, byte>(a), new Dictionary<int, byte>(a)))
            .Faster(
                t =>
                {
                    var m = t.Item2;
                    foreach (var (k, _) in t.a) m.IndexOf(k);
                },
                t =>
                {
                    var m = t.Item3;
                    foreach (var (k, _) in t.a) m.ContainsKey(k);
                },
                repeat: 100
            ).Output(writeLine);
        }
    }


    public class ListSlim<T> : IReadOnlyList<T>
    {
        static readonly T[] empty = Array.Empty<T>();
        T[] entries;
        int count;
        public ListSlim() => entries = empty;
        public ListSlim(int capacity) => entries = new T[capacity];
        public ListSlim(IEnumerable<T> items)
        {
            if (items is ICollection<T> ts)
            {
                entries = new T[ts.Count];
                ts.CopyTo(entries, 0);
            }
            else entries = items.ToArray();
            count = entries.Length;
        }
        public int Count => count;
        public T this[int i]
        {
            get => entries[i];
            set => entries[i] = value;
        }


        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithResize(T item)
        {
            int c = count;
            if (c == 0) entries = new T[4];
            else
            {
                var newEntries = new T[c * 2];
                Array.Copy(entries, 0, newEntries, 0, c);
                entries = newEntries;
            }
            entries[c] = item;
            count = c + 1;
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
        static class SetSlimHolder { internal static Entry[] Initial = new Entry[1]; }
        struct Entry { internal int Bucket; internal int Next; internal T Item; }
        int count;
        Entry[] entries;
        public SetSlim() => entries = SetSlimHolder.Initial;

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

        void Resize()
        {
            var oldEntries = entries;
            var newEntries = new Entry[oldEntries.Length * 2];
            int i = oldEntries.Length;
            while (i-- > 0)
            {
                newEntries[i].Item = oldEntries[i].Item;
                var bucketIndex = newEntries[i].Item.GetHashCode() & (newEntries.Length - 1);
                newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
                newEntries[bucketIndex].Bucket = i + 1;
            }
            entries = newEntries;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        int AddItem(T item, int hashCode)
        {
            var i = count;
            if (i == 0 && entries.Length == 1) entries = new Entry[2];
            else if (i == entries.Length) Resize();
            var ent = entries;
            ent[i].Item = item;
            var bucketIndex = hashCode & (ent.Length - 1);
            ent[i].Next = ent[bucketIndex].Bucket - 1;
            ent[bucketIndex].Bucket = i + 1;
            count++;
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
        static class SetSlimHolder { internal static Entry[] Initial = new Entry[1]; }
        struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
        int count;
        Entry[] entries;
        public MapSlim() => entries = SetSlimHolder.Initial;

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

        void Resize()
        {
            var oldEntries = entries;
            var newEntries = new Entry[oldEntries.Length * 2];
            int i = oldEntries.Length;
            while (i-- > 0)
            {
                newEntries[i].Key = oldEntries[i].Key;
                newEntries[i].Value = oldEntries[i].Value;
                var bucketIndex = newEntries[i].Key.GetHashCode() & (newEntries.Length - 1);
                newEntries[i].Next = newEntries[bucketIndex].Bucket - 1;
                newEntries[bucketIndex].Bucket = i + 1;
            }
            entries = newEntries;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void AddItem(K key, V value, int hashCode)
        {
            var i = count;
            if (i == 0 && entries.Length == 1) entries = new Entry[2];
            else if (i == entries.Length) Resize();
            var ent = entries;
            ent[i].Key = key;
            ent[i].Value = value;
            var bucketIndex = hashCode & (ent.Length - 1);
            ent[i].Next = ent[bucketIndex].Bucket - 1;
            ent[bucketIndex].Bucket = i + 1;
            count++;
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
                yield return KeyValuePair.Create(entries[i].Key, entries[i].Value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}