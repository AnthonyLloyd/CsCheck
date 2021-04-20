using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CsCheck;

public static class Dbg
{
    static ListSlim<string> info = new();
    static MapSlim<string, int> count = new();
    static MapSlim<string, Action> function = new();

    public static void Output(Action<string> output)
    {
        foreach (var s in info)
            output(string.Concat("Dbg: ", s));
        int maxLength = 0, totalCount = 0;
        foreach (var kv in count)
        {
            totalCount += kv.Value;
            if (kv.Key.Length > maxLength)
                maxLength = kv.Key.Length;
        }
        foreach (var kc in count.OrderByDescending(i => i.Value))
        {
            var percent = ((float)kc.Value / totalCount).ToString("0.0%").PadLeft(7);
            output(string.Concat(kc.Key.PadRight(maxLength), percent, " ", kc.Value));
        }
        Clear();
    }

    public static void Clear()
    {
        info = new();
        count = new();
        function = new();
    }

    public static void Add(string s)
    {
        lock (info) info.Add(s);
    }

    public static void Count<T>(T t)
    {
        var s = Check.Print(t);
        lock (count) count.GetValueOrNullRef(s)++;
    }

    public static IEnumerable<T> Debug<T>(this IEnumerable<T> source, string name)
    {
        foreach (var t in source)
        {
            Add(string.Concat(name, " : ", Check.Print(t)));
            yield return t;
        }
    }

    public static Gen<T> DebugClassify<T>(this Gen<T> gen, Func<T, string> name) =>
        Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        var t = gen.Generate(pcg, min, out size);
        Count(name(t));
        return t;
    });

    public static void Add<T>(string name, Action<T> f, T t)
    {
        Add(string.Concat(name, " : ", Check.Print(t)));
        f(t);
    }

    public static void Add<T1, T2>(string name, Action<T1, T2> f, T1 t1, T2 t2)
    {
        Add(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2)));
        f(t1, t2);
    }

    public static void Add<T1, T2, T3>(string name, Action<T1, T2, T3> f, T1 t1, T2 t2, T3 t3)
    {
        Add(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2), ", ", Check.Print(t3)));
        f(t1, t2, t3);
    }

    public static R Add<R>(string name, Func<R> f)
    {
        var r = f();
        Add(string.Concat(name, " : ", Check.Print(r)));
        return r;
    }

    public static R Add<T, R>(string name, Func<T, R> f, T t)
    {
        var r = f(t);
        Add(string.Concat(name, " : ", Check.Print(t), " -> ", Check.Print(r)));
        return r;
    }

    public static R Add<T1, T2, R>(string name, Func<T1, T2, R> f, T1 t1, T2 t2)
    {
        var r = f(t1, t2);
        Add(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(r)));
        return r;
    }

    public static R Add<T1, T2, T3, R>(string name, Func<T1, T2, T3, R> f, T1 t1, T2 t2, T3 t3)
    {
        var r = f(t1, t2, t3);
        Add(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(t3), " -> ", Check.Print(r)));
        return r;
    }

    public static void Function(string name, Action action)
    {
        lock (function) function[name] = action;
    }

    public static void Function(string name) => function[name]();

    class ListSlim<T> : IReadOnlyList<T>
    {
        static class Holder { internal static T[] Initial = Array.Empty<T>(); }
        T[] entries;
        int count;
        public ListSlim() => entries = Holder.Initial;
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
            else AddWithResize(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return entries[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    class MapSlim<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
    {
        struct Entry { internal int Bucket; internal int Next; internal K Key; internal V Value; }
        static class Holder { internal static Entry[] Initial = new Entry[1]; }
        int count;
        Entry[] entries;
        public MapSlim() => entries = Holder.Initial;
        public int Count => count;

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
            if (i >= 0) return ref ent[i].Value;
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

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return new(entries[i].Key, entries[i].Value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}