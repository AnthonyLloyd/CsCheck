using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CsCheck;

/// <summary>Debug utility functions to collect, count and output debug info, classify generators, define and remotely call functions, and perform in code regression testing.
/// CsCheck can temporarily be added as a reference to run in non test code.
/// Note this module is only for temporary debug use and the API may change between minor versions.</summary>
public static class Dbg
{
    static ListSlim<string> info = new();
    static MapSlim<string, int> counts = new();
    static MapSlim<string, object> objects = new();
    static MapSlim<string, Action> functions = new();

    /// <summary>Debugger break.</summary>
    public static void Break() => System.Diagnostics.Debugger.Break();

    /// <summary>Output held debug info.</summary>
    public static void Output(Action<string> output)
    {
        foreach (var s in info)
            output(string.Concat("Dbg: ", s));
        int maxLength = 0, totalCount = 0;
        foreach (var kv in counts)
        {
            totalCount += kv.Value;
            if (kv.Key.Length > maxLength)
                maxLength = kv.Key.Length;
        }
        foreach (var kc in counts.OrderByDescending(i => i.Value))
        {
            var percent = ((float)kc.Value / totalCount).ToString("0.0%").PadLeft(7);
            output(string.Concat(kc.Key.PadRight(maxLength), percent, " ", kc.Value));
        }
        Clear();
    }

    /// <summary>Clear debug info.</summary>
    public static void Clear()
    {
        info = new();
        counts = new();
        objects = new();
        functions = new();
        if(regressionStream != null)
        {
            regressionStream.Close();
            regressionStream = null;
        }
    }

    /// <summary>Add debug info.</summary>
    public static void Info<T>(T t)
    {
        lock (info) info.Add(t.ToString());
    }

    /// <summary>Save object by name.</summary>
    public static object Set(string name, object o) => objects[name] = o;

    /// <summary>Get object by name.</summary>
    public static object Get(string name) => objects[name];

    /// <summary>Increment debug info counter.</summary>
    public static void Count<T>(T t)
    {
        var s = Check.Print(t);
        lock (counts) counts.GetValueOrNullRef(s)++;
    }

    /// <summary>Add IEnumerable item debug info.</summary>
    public static IEnumerable<T> Debug<T>(this IEnumerable<T> source, string name)
    {
        foreach (var t in source)
        {
            Info(string.Concat(name, " : ", Check.Print(t)));
            yield return t;
        }
    }

    /// <summary>Classify and count generated types debug info.</summary>
    public static Gen<T> DebugClassify<T>(this Gen<T> gen, Func<T, string> name) =>
        Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        var t = gen.Generate(pcg, min, out size);
        Count(name(t));
        return t;
    });

    /// <summary>Method debug info.</summary>
    public static void Info<T>(string name, Action<T> f, T t)
    {
        Info(string.Concat(name, " : ", Check.Print(t)));
        f(t);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2>(string name, Action<T1, T2> f, T1 t1, T2 t2)
    {
        Info(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2)));
        f(t1, t2);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2, T3>(string name, Action<T1, T2, T3> f, T1 t1, T2 t2, T3 t3)
    {
        Info(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2), ", ", Check.Print(t3)));
        f(t1, t2, t3);
    }

    /// <summary>Function debug info.</summary>
    public static R Info<R>(string name, Func<R> f)
    {
        var r = f();
        Info(string.Concat(name, " : ", Check.Print(r)));
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T, R>(string name, Func<T, R> f, T t)
    {
        var r = f(t);
        Info(string.Concat(name, " : ", Check.Print(t), " -> ", Check.Print(r)));
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, R>(string name, Func<T1, T2, R> f, T1 t1, T2 t2)
    {
        var r = f(t1, t2);
        Info(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(r)));
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, T3, R>(string name, Func<T1, T2, T3, R> f, T1 t1, T2 t2, T3 t3)
    {
        var r = f(t1, t2, t3);
        Info(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(t3), " -> ", Check.Print(r)));
        return r;
    }

    /// <summary>Define and store debug call by name.</summary>
    public static void CallAdd(string name, Action action)
    {
        lock (functions) functions[name] = action;
    }

    /// <summary>Call a stored debug call.</summary>
    public static void Call(string name) => functions[name]();

    static RegressionStream regressionStream;
    /// <summary>Saves a sequence of values on the first run and compares them on subsequent runs.</summary>
    public static RegressionStream Regression => regressionStream ??= new RegressionStream(Path.Combine(Hash.CacheDir, "Dbg.Regression.has"));

    public class RegressionStream : IAdd
    {
        readonly string filename;
        readonly bool reading;
        readonly FileStream stream;
        string lastString = "null";
        double absolute = 1e-12, relative = 1e-9;
        public RegressionStream(string filename)
        {
            this.filename = filename;
            reading = File.Exists(filename);
            if (reading) stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            else Directory.CreateDirectory(Path.GetDirectoryName(filename));
        }

        public void Delete()
        {
            regressionStream = null;
            stream?.Dispose();
            if (File.Exists(filename)) File.Delete(filename);
        }

        public void Close()
        {
            if (reading && stream.Length != stream.Position)
                throw new CsCheckException($"file (length {stream.Length}) contains more data than read (length {stream.Position})");
            regressionStream = null;
            stream?.Dispose();
        }

        public void Tolerance(double absolute = 0.0, double relative = 0.0)
        {
            this.absolute = absolute;
            this.relative = relative;
        }

        public void Add(bool val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadBool(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteBool(stream, val);
            }
        }

        public void Add(sbyte val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadSByte(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteSByte(stream, val);
            }
        }

        public void Add(byte val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadByte(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteByte(stream, val);
            }
        }

        public void Add(short val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadShort(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteShort(stream, val);
            }
        }

        public void Add(ushort val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadUShort(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteUShort(stream, val);
            }
        }

        public void Add(int val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadInt(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteInt(stream, val);
            }
        }

        public void Add(uint val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadUInt(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteUInt(stream, val);
            }
        }

        public void Add(long val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadLong(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteLong(stream, val);
            }
        }

        public void Add(ulong val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadULong(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteULong(stream, val);
            }
        }

        public void Add(DateTime val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDateTime(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteDateTime(stream, val);
            }
        }

        public void Add(TimeSpan val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadTimeSpan(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteTimeSpan(stream, val);
            }
        }

        public void Add(DateTimeOffset val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDateTimeOffset(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteDateTimeOffset(stream, val);
            }
        }

        public void Add(Guid val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadGuid(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteGuid(stream, val);
            }
        }

        public void Add(char val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadChar(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteChar(stream, val);
            }
        }

        public void Add(string val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadString(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteString(stream, val);
            }
            lastString = val;
        }

        public void Add(double val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDouble(stream);
                if (Math.Abs(val - val2) > absolute + relative * (Math.Abs(val) + Math.Abs(val2)))
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteDouble(stream, val);
            }
        }

        public void Add(float val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadFloat(stream);
                if (Math.Abs(val - val2) > absolute + relative * (Math.Abs(val) + Math.Abs(val2)))
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteFloat(stream, val);
            }
        }

        public void Add(decimal val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDecimal(stream);
                if ((double)Math.Abs(val - val2) > absolute + relative * (double)(Math.Abs(val) + Math.Abs(val2)))
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {lastString})");
            }
            else
            {
                using var stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                Hash.StreamSerializer.WriteDecimal(stream, val);
            }
        }

        public void Add(IEnumerable<bool> val)
        {
            var col = val as ICollection<bool> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<byte> val)
        {
            var col = val as ICollection<byte> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<sbyte> val)
        {
            var col = val as ICollection<sbyte> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<short> val)
        {
            var col = val as ICollection<short> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<ushort> val)
        {
            var col = val as ICollection<ushort> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<int> val)
        {
            var col = val as ICollection<int> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<uint> val)
        {
            var col = val as ICollection<uint> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<long> val)
        {
            var col = val as ICollection<long> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<ulong> val)
        {
            var col = val as ICollection<ulong> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<DateTime> val)
        {
            var col = val as ICollection<DateTime> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<DateTimeOffset> val)
        {
            var col = val as ICollection<DateTimeOffset> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<TimeSpan> val)
        {
            var col = val as ICollection<TimeSpan> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<Guid> val)
        {
            var col = val as ICollection<Guid> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<char> val)
        {
            var col = val as ICollection<char> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<string> val)
        {
            var col = val as ICollection<string> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<double> val)
        {
            var col = val as ICollection<double> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<float> val)
        {
            var col = val as ICollection<float> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
        public void Add(IEnumerable<decimal> val)
        {
            var col = val as ICollection<decimal> ?? val.ToArray();
            Add((uint)col.Count);
            foreach (var v in col) Add(v);
        }
    }

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