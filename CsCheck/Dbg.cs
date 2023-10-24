// Copyright 2023 Anthony Lloyd
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CsCheck;

/// <summary>Debug utility functions to collect, count and output debug info, time, classify generators, define and remotely call functions, and perform in code regression testing.
/// CsCheck can temporarily be added as a reference to run in non test code.
/// Note this module is only for temporary debug use and the API may change between minor versions.</summary>
public static class Dbg
{
    static ListSlim<string> info = new();
    static MapSlim<string, int> counts = new();
    static MapSlim<string, object?> objects = new();
    static MapSlim<string, Action> functions = new();
    static MapSlim<string, (long, int)> times = new();
    static Action<string>? autoOutput;
    static bool autoEveryInfo;

    /// <summary>Debugger break.</summary>
    public static void Break() => Debugger.Break();

    public static void AutoOutput(Action<string> output, bool everyInfo = true)
    {
        autoOutput = output;
        autoEveryInfo = everyInfo;
    }

    public static void Flush()
    {
        if (autoOutput is not null) Output(autoOutput);
    }

    /// <summary>Output held debug info.</summary>
    public static IEnumerable<string> Output()
    {
        foreach (var s in info)
            yield return string.Concat("[Dbg] ", s);
        int maxLength = 0, total = 0;
        foreach (var kv in counts)
        {
            total += kv.Value;
            if (kv.Key.Length > maxLength) maxLength = kv.Key.Length;
        }
        foreach (var kc in counts.OrderByDescending(i => i.Value))
        {
            var percent = ((float)kc.Value / total).ToString("0.00%").PadLeft(7);
            yield return string.Concat("Count: ", kc.Key.PadRight(maxLength), percent, " ", kc.Value);
        }
        maxLength = 0;
        int maxPercent = 0, maxTime = 0, maxCount = 0;
        foreach (var kv in times)
        {
            if (kv.Key.Length > maxLength) maxLength = kv.Key.Length;
            if ((kv.Value.Item1 * 1000L / Stopwatch.Frequency).ToString("#,0").Length > maxTime)
                maxTime = (kv.Value.Item1 * 1000L / Stopwatch.Frequency).ToString("#,0").Length;
            if (((float)kv.Value.Item1 / times.Value(0).Item1).ToString("0.0%").Length > maxPercent)
                maxPercent = ((float)kv.Value.Item1 / times.Value(0).Item1).ToString("0.0%").Length;
            if (kv.Value.Item2.ToString().Length > maxCount)
                maxCount = kv.Value.Item2.ToString().Length;
        }
        foreach (var kc in times)
        {
            var time = (kc.Value.Item1 * 1000L / Stopwatch.Frequency).ToString("#,0").PadLeft(maxTime + 1);
            var percent = ((float)kc.Value.Item1 / times.Value(0).Item1).ToString("0.0%").PadLeft(maxPercent + 1);
            var count = kc.Value.Item2.ToString().PadLeft(maxCount + 1);
            yield return string.Concat("Time: ", kc.Key.PadRight(maxLength), time, "ms", percent, count);
        }
        Clear();
    }

    /// <summary>Output held debug info.</summary>
    public static void Output(Action<string> output)
    {
        foreach (var s in Output())
            output(s);
    }

    /// <summary>Clear debug info.</summary>
    public static void Clear()
    {
        info = new();
        counts = new();
        objects = new();
        functions = new();
        times = new();
        if (regressionStream is not null)
        {
            regressionStream.Dispose();
            regressionStream = null;
        }
    }

    /// <summary>Save object by name.</summary>
    public static void Set(string name, object o) => objects[name] = o;

    /// <summary>Save object by name.</summary>
    public static T DbgSet<T>(this T t, string name)
    {
        objects[name] = t;
        return t;
    }

    /// <summary>Get object by name.</summary>
    public static object? Get(string name) => objects[name];

    /// <summary>Increment debug info counter. Function name when parameter not set.</summary>
    public static void Count<T>(T t)
    {
        var s = Check.Print(t);
        lock (counts) counts.GetValueOrNullRef(s!)++;
    }

    /// <summary>Increment debug info counter. Function name when parameter not set.</summary>
    public static void Count([CallerMemberName] string name = "", [CallerLineNumber] int line = 0) => Count(string.Concat(name, " ", line));

    public struct TimeRegion : IDisposable
    {
        public string Name;
        public long Start;

        /// <summary>End the time measurement.</summary>
        public readonly void End()
        {
            var timestamp = Stopwatch.GetTimestamp();
            lock (times) times.GetValueOrNullRef(Name).Item1 += timestamp - Start;
            if (autoOutput is not null) Output(autoOutput);
        }

        /// <summary>Record time to this line.</summary>
        public readonly void Line([CallerLineNumber] int line = 0)
        {
            var timestamp = Stopwatch.GetTimestamp();
            lock (times)
            {
                ref var time = ref times.GetValueOrNullRef(string.Concat(Name, " line ", line.ToString()));
                time.Item1 += timestamp - Start;
                time.Item2++;
            }
        }

        public readonly void Dispose() => End();

        /// <summary>End the time measurement and start a new one.</summary>
        public readonly TimeRegion EndStart<T>(T t)
        {
            End();
            return Time(t);
        }
    }

    /// <summary>Start a time measurement.</summary>
    public static TimeRegion Time<T>(T t)
    {
        var name = Check.Print(t);
        lock (times) times.GetValueOrNullRef(name!).Item2++;
        return new() { Name = name!, Start = Stopwatch.GetTimestamp() };
    }

    /// <summary>Start a time measurement. Function name when parameter not set.</summary>
    public static TimeRegion Time([CallerMemberName] string name = "") => Time<string>(name);

    sealed class GenDbgClassify<T>(Gen<T> gen, Func<T, string> name) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var t = gen.Generate(pcg, min, out size);
            Count(name(t));
            return t;
        }
    }
    /// <summary>Classify and count generated types debug info.</summary>
    public static Gen<T> DbgClassify<T>(this Gen<T> gen, Func<T, string> name)
        => new GenDbgClassify<T>(gen, name);

    /// <summary>Add debug info.</summary>
    public static void Info<T>(T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = string.Concat(name, " ", line, ": ", Check.Print(t));
        lock (info)
        {
            info.Add(s);
        }
        if (autoEveryInfo && autoOutput is not null) Output(autoOutput);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T>(Action<T> f, T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = string.Concat(Check.Print(t), " -> ()");
        Info(s, name, line);
        f(t);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2>(Action<T1, T2> f, T1 t1, T2 t2, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = string.Concat(Check.Print(t1), " -> ", Check.Print(t2), " -> ()");
        Info(s, name, line);
        f(t1, t2);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2, T3>(Action<T1, T2, T3> f, T1 t1, T2 t2, T3 t3, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = string.Concat(Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(t3), " -> ()");
        Info(s, name, line);
        f(t1, t2, t3);
    }

    /// <summary>Function debug info.</summary>
    public static R Info<R>(Func<R> f, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f();
        var s = string.Concat("() -> ", Check.Print(r));
        Info(s, name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T, R>(Func<T, R> f, T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t);
        Info(string.Concat(Check.Print(t), " -> ", Check.Print(r)), name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, R>(Func<T1, T2, R> f, T1 t1, T2 t2, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t1, t2);
        Info(string.Concat(Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(r)), name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, T3, R>(Func<T1, T2, T3, R> f, T1 t1, T2 t2, T3 t3, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t1, t2, t3);
        Info(string.Concat(Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(t3), " -> ", Check.Print(r)), name, line);
        return r;
    }

    public static T DbgInfo<T>(this T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        Info(t, name, line);
        return t;
    }

    /// <summary>Define and store debug call by name.</summary>
    public static void CallAdd(string name, Action action)
    {
        lock (functions) functions[name] = action;
    }

    /// <summary>Call a stored debug call.</summary>
    public static void Call(string name) => functions[name]();

    /// <summary>Perform an action inline and return the input.</summary>
    public static T DbgTee<T>(this T t, Action<T> action)
    {
        action(t);
        return t;
    }

    /// <summary>Perform an action inline and return the input.</summary>
    public static R DbgTee<T, R>(this R r, Action<T, R> action, T t)
    {
        action(t, r);
        return r;
    }

    /// <summary>Perform an action inline and return the input.</summary>
    public static R DbgTee<T1, T2, R>(this R r, Action<T1, T2, R> action, T1 t1, T2 t2)
    {
        action(t1, t2, r);
        return r;
    }

    public static IEnumerable<T> DbgCache<T>(this IEnumerable<T> e)
    {
        return new CachedEnumerable<T>(e);
    }

    sealed class CachedEnumerable<T>(IEnumerable<T> enumerable) : IEnumerable<T>, IDisposable
    {
        IEnumerator<T>? _enumerator = enumerable.GetEnumerator();
        readonly List<T> _cache = [];
        public IEnumerator<T> GetEnumerator()
        {
            int index = 0;
            for (; index < _cache.Count; index++) yield return _cache[index];
            for (; _enumerator?.MoveNext() == true; index++)
            {
                var current = _enumerator.Current;
                _cache.Add(current);
                yield return current;
            }
            if (_enumerator is not null)
            {
                _enumerator.Dispose();
                _enumerator = null;
            }
            for (; index < _cache.Count; index++) yield return _cache[index];
        }
        public void Dispose()
        {
            if (_enumerator is not null)
            {
                _enumerator.Dispose();
                _enumerator = null;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static RegressionStream? regressionStream;
    /// <summary>Saves a sequence of values on the first run and compares them on subsequent runs.</summary>
    public static RegressionStream Regression => regressionStream ??= new RegressionStream(Path.Combine(Hash.CacheDir, "Dbg.Regression.has"));

    public sealed class RegressionStream : IRegression, IDisposable
    {
        readonly string filename;
        readonly bool reading;
        readonly FileStream stream;
        string lastString = "null";
        double absolute = 1e-12, relative = 1e-9;
        public RegressionStream(string filename)
        {
            this.filename = filename;
            if (File.Exists(filename))
            {
                stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
                reading = true;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
                stream = File.Open(filename, FileMode.Append, FileAccess.Write, FileShare.None);
                reading = false;
            }
        }

        public void Delete()
        {
            regressionStream = null;
            stream.Dispose();
            if (File.Exists(filename)) File.Delete(filename);
        }

        public void Dispose()
        {
            if (reading && stream.Length != stream.Position)
                throw new CsCheckException($"file (length {stream.Length}) contains more data than read (length {stream.Position})");
            regressionStream = null;
            stream.Dispose();
        }

        public void Close() => Dispose();

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
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteBool(stream, val);
            }
        }

        public void Add(sbyte val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadSByte(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteSByte(stream, val);
            }
        }

        public void Add(byte val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadByte(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteByte(stream, val);
            }
        }

        public void Add(short val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadShort(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteShort(stream, val);
            }
        }

        public void Add(ushort val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadUShort(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteUShort(stream, val);
            }
        }

        public void Add(int val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadInt(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteInt(stream, val);
            }
        }

        public void Add(uint val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadUInt(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteUInt(stream, val);
            }
        }

        public void Add(long val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadLong(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteLong(stream, val);
            }
        }

        public void Add(ulong val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadULong(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteULong(stream, val);
            }
        }

        public void Add(DateTime val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDateTime(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteDateTime(stream, val);
            }
        }

        public void Add(TimeSpan val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadTimeSpan(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteTimeSpan(stream, val);
            }
        }

        public void Add(DateTimeOffset val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDateTimeOffset(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteDateTimeOffset(stream, val);
            }
        }

        public void Add(Guid val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadGuid(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteGuid(stream, val);
            }
        }

        public void Add(char val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadChar(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual '{val}' but Expected '{val2}'. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteChar(stream, val);
            }
        }

        public void Add(string val)
        {
            val ??= "null";
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadString(stream);
                if (val != val2)
                    throw new CsCheckException($"Actual '{val}' but Expected '{val2}'. (last string was {LastString})");
            }
            else
            {
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
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteDouble(stream, val);
            }
        }

        public void Add(float val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadFloat(stream);
                if (Math.Abs(val - val2) > absolute + relative * (Math.Abs(val) + Math.Abs(val2)))
                    throw new CsCheckException($"Actual {val} but Expected {val2}. (last string was {LastString})");
            }
            else
            {
                Hash.StreamSerializer.WriteFloat(stream, val);
            }
        }

        public void Add(decimal val)
        {
            if (reading)
            {
                var val2 = Hash.StreamSerializer.ReadDecimal(stream);
                if ((double)Math.Abs(val - val2) > absolute + relative * (double)(Math.Abs(val) + Math.Abs(val2)))
                    throw new CsCheckException($"Actual {val} but Expected {val2}. `");
            }
            else
            {
                Hash.StreamSerializer.WriteDecimal(stream, val);
            }
        }

        string LastString => lastString == "null" ? "null" : $"'{lastString}'";
    }

    sealed class ListSlim<T> : IReadOnlyList<T>
    {
        static class Holder { internal static T[] Initial = []; }
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
            else
            {
                AddWithResize(item);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return entries[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class MapSlim<K, V> : IReadOnlyCollection<KeyValuePair<K, V>> where K : IEquatable<K>
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
                ent[i].Value = default!;
                ent[bucketIndex].Bucket = ++count;
                return ref ent[i].Value;
            }
        }

        public V Value(int i) => entries[i].Value;

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return new(entries[i].Key, entries[i].Value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static class RegressionExtensions
{
    const string NULL = "<null>";

    public static void Add(this IRegression r, IEnumerable<bool> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<bool> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<byte> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<byte> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<char> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<char> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<DateTime> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<DateTime> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<DateTimeOffset> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<DateTimeOffset> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<decimal> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<decimal> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<double> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<double> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<float> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<float> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<Guid> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<Guid> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<int> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<int> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<long> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<long> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<sbyte> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<sbyte> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<short> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<short> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<string> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<string> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<TimeSpan> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<TimeSpan> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<uint> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<uint> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<ulong> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<ulong> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<ushort> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<ushort> ?? val.ToArray();
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }
}