// Copyright 2026 Anthony Lloyd
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

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CsCheck;
using System.Runtime.InteropServices;

#pragma warning disable CA1050 // Declare types in namespaces

/// <summary>Debug utility functions to collect, count and output debug info, time, classify generators, define and remotely call functions, and perform in code regression testing.
/// CsCheck can temporarily be added as a reference to run in non test code.
/// Note this module is only for temporary debug use and the API may change between minor versions.</summary>
public static class Dbg
{
    static ListSlim<string> info = new();
    static MapSlim<string, int> counts = new();
    static MapSlim<string, object?> objects = new();
    static MapSlim<string, Action> functions = new();
    static MapSlim<string, (MedianEstimator, List<long>)> times = new();
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
            yield return $"[Dbg] {s}";
        int maxLength = 0, total = 0;
        foreach (var kv in counts)
        {
            total += kv.Value;
            if (kv.Key.Length > maxLength) maxLength = kv.Key.Length;
        }
        foreach (var kc in counts.OrderByDescending(i => i.Value))
        {
            var percent = ((float)kc.Value / total).ToString("0.00%").PadLeft(7);
            yield return $"Count: {kc.Key.PadRight(maxLength)}{percent} {kc.Value}";
        }
        maxLength = 0;
        int maxPercent = 0, maxTime = 0, maxCount = 0;
        foreach (var kv in times)
        {
            if (kv.Key.Length > maxLength) maxLength = kv.Key.Length;
            if ((kv.Value.Item1.Median * 1000L / Stopwatch.Frequency).ToString("#,0").Length > maxTime)
                maxTime = (kv.Value.Item1.Median * 1000L / Stopwatch.Frequency).ToString("#,0").Length;
            if (((float)kv.Value.Item1.Median / times.Value(0).Item1.Median).ToString("0.0%").Length > maxPercent)
                maxPercent = ((float)kv.Value.Item1.Median / times.Value(0).Item1.Median).ToString("0.0%").Length;
            if (kv.Value.Item1.N.ToString().Length > maxCount)
                maxCount = kv.Value.Item1.N.ToString().Length;
        }
        foreach (var kc in times)
        {
            var time = (kc.Value.Item1.Median * 1000L / Stopwatch.Frequency).ToString("#,0").PadLeft(maxTime + 1);
            var percent = ((float)kc.Value.Item1.Median / times.Value(0).Item1.Median).ToString("0.0%").PadLeft(maxPercent + 1);
            var count = kc.Value.Item1.N.ToString().PadLeft(maxCount + 1);
            yield return $"Time: {kc.Key.PadRight(maxLength)}{time}ms{percent}{count}";
        }
        Clear();
    }

    public static KeyValuePair<string, (MedianEstimator Completed, MedianEstimator Running)>[] OutputTimeStats(bool reset = true)
    {
        var stats = reset ? Interlocked.Exchange(ref times, new()) : times;
        lock (stats)
        {
            var now = Stopwatch.GetTimestamp();
            return [.. stats.Select(i =>
            {
                var (completed, starts) = i.Value;
                lock (starts)
                {
                    var running = new MedianEstimator();
                    foreach (var start in starts)
                        running.Add(now - start);
                    return KeyValuePair.Create(i.Key, (completed, running));
                }
            })];
        }
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
        regressionStream?.Dispose();
        regressionStream = null;
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
    public static void Count([CallerMemberName] string name = "", [CallerLineNumber] int line = 0) => Count($"{name} {line}");

    public struct TimeRegion : IDisposable
    {
        public string Name;
        public long Start;

        /// <summary>End the time measurement.</summary>
        public readonly void End()
        {
            var timestamp = Stopwatch.GetTimestamp();
            MedianEstimator estimator;
            List<long> starts;
            lock (times)
            {
                ref var time = ref times.GetValueOrNullRef(Name);
                if (time.Item1 is null)
                {
                    time.Item1 = estimator = new();
                    time.Item2 = starts = [];
                }
                else
                {
                    (estimator, starts) = time;
                }
            }
            lock (starts)
                starts.Remove(Start);
            lock (estimator)
                estimator.Add(timestamp - Start);
            if (autoOutput is not null) Output(autoOutput);
        }

        /// <summary>Record time to this line.</summary>
        public readonly void Line([CallerLineNumber] int line = 0)
        {
            var timestamp = Stopwatch.GetTimestamp();
            MedianEstimator estimator;
            lock (times)
            {
                ref var time = ref times.GetValueOrNullRef($"{Name} line {line}");
                if (time.Item1 is null)
                {
                    time.Item1 = estimator = new();
                    time.Item2 = [];
                }
                else
                {
                    estimator = time.Item1;
                }
            }
            lock (estimator)
                estimator.Add(timestamp - Start);
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
        List<long> starts;
        lock (times)
        {
            ref var time = ref times.GetValueOrNullRef(name!);
            if (time.Item1 is null)
            {
                time.Item1 = new();
                time.Item2 = starts = [];
            }
            else
            {
                starts = time.Item2;
            }
        }
        var start = Stopwatch.GetTimestamp();
        lock(starts)
            starts.Add(start);
        return new() { Name = name!, Start = start };
    }

    /// <summary>Start a time measurement. Function name when parameter not set.</summary>
    public static TimeRegion Time([CallerMemberName] string name = "") => Time<string>(name);

    /// <summary>Add debug info.</summary>
    public static void Info<T>(T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = $"{name} {line}: {Check.Print(t)}";
        lock (info)
        {
            info.Add(s);
        }
        if (autoEveryInfo && autoOutput is not null) Output(autoOutput);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T>(Action<T> f, T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = $"{Check.Print(t)} -> ()";
        Info(s, name, line);
        f(t);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2>(Action<T1, T2> f, T1 t1, T2 t2, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = $"{Check.Print(t1)} -> {Check.Print(t2)} -> ()";
        Info(s, name, line);
        f(t1, t2);
    }

    /// <summary>Method debug info.</summary>
    public static void Info<T1, T2, T3>(Action<T1, T2, T3> f, T1 t1, T2 t2, T3 t3, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var s = $"{Check.Print(t1)} -> {Check.Print(t2)} -> {Check.Print(t3)} -> ()";
        Info(s, name, line);
        f(t1, t2, t3);
    }

    /// <summary>Function debug info.</summary>
    public static R Info<R>(Func<R> f, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f();
        var s = $"() -> {Check.Print(r)}";
        Info(s, name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T, R>(Func<T, R> f, T t, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t);
        Info($"{Check.Print(t)} -> {Check.Print(r)}", name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, R>(Func<T1, T2, R> f, T1 t1, T2 t2, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t1, t2);
        Info($"{Check.Print(t1)} -> {Check.Print(t2)} -> {Check.Print(r)}", name, line);
        return r;
    }

    /// <summary>Function debug info.</summary>
    public static R Info<T1, T2, T3, R>(Func<T1, T2, T3, R> f, T1 t1, T2 t2, T3 t3, [CallerMemberName] string name = "", [CallerLineNumber] int line = 0)
    {
        var r = f(t1, t2, t3);
        Info($"{Check.Print(t1)} -> {Check.Print(t2)} -> {Check.Print(t3)} -> {Check.Print(r)}", name, line);
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
            _enumerator?.Dispose();
            _enumerator = null;
            for (; index < _cache.Count; index++) yield return _cache[index];
        }
        public void Dispose()
        {
            _enumerator?.Dispose();
            _enumerator = null;
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
                ThrowHelper.Throw($"file (length {stream.Length}) contains more data than read (length {stream.Position})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual '{val}' but Expected '{val2}'. (last string was {LastString})");
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
                if (!string.Equals(val, val2))
                    ThrowHelper.Throw($"Actual '{val}' but Expected '{val2}'. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. (last string was {LastString})");
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
                    ThrowHelper.Throw($"Actual {val} but Expected {val2}. `");
            }
            else
            {
                Hash.StreamSerializer.WriteDecimal(stream, val);
            }
        }

        string LastString => string.Equals(lastString, "null") ? "null" : $"'{lastString}'";
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
        [StructLayout(LayoutKind.Auto)]
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
            i = count;
            if (ent.Length == i || ent.Length == 1) ent = Resize();
            var bucketIndex = hashCode & (ent.Length - 1);
            ent[i].Next = ent[bucketIndex].Bucket - 1;
            ent[i].Key = key;
            ent[i].Value = default!;
            ent[bucketIndex].Bucket = ++count;
            return ref ent[i].Value;
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
        var col = val as IReadOnlyCollection<bool> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<byte> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<byte> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<char> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<char> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<DateTime> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<DateTime> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<DateTimeOffset> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<DateTimeOffset> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<decimal> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<decimal> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<double> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as ICollection<double> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<float> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<float> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<Guid> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<Guid> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<int> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<int> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<long> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<long> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<sbyte> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<sbyte> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<short> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<short> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<string> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<string> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<TimeSpan> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<TimeSpan> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<uint> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<uint> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<ulong> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<ulong> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }

    public static void Add(this IRegression r, IEnumerable<ushort> val)
    {
        if (val is null) { r.Add(NULL); return; }
        var col = val as IReadOnlyCollection<ushort> ?? [.. val];
        r.Add((uint)col.Count);
        foreach (var v in col) r.Add(v);
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Observations from a bug-hunting pass, parked here for later. Nothing below is acted on. Everything is measured rather
// than inferred unless it says otherwise. Delete this block once it has been read.
//
// FOUND, NOT FIXED
//
// 1. Check.Equal on a rank 2 array versus a non-array is three-way inconsistent. Measured over 20 ordered pairs of a
//    2x2 int[,] against collections holding 1,2,3,4:
//        vs int[]                                                          -> false (correct)
//        vs List, Collection, ReadOnlyCollection, ImmutableArray,
//           ImmutableList, ArrayList                          (12 pairs)   -> throws ArgumentException
//        vs HashSet, Queue, Enumerable.Range                  (6 pairs)    -> true
//    Cause is in Utils.Equal: the rank 2 branch only fires when both sides are Array, so a T[,] falls through to
//    "a is IList ail", and a rank 2 array's IList indexer throws. Anything that is not an IList falls further to the
//    flattening compare, which casts the 2x2 to [1,2,3,4] and calls it equal. The true cases look worse than the
//    throwing ones: the answer depends on the concrete type of the other side. Two skipped tests in CheckTests.cs
//    (Equal_2D_Array_Versus_IList_..., Equal_2D_Array_Versus_A_Flat_Sequence_...) assert what I would expect instead.
//
// 2. Causal's Time% column disagrees with its speedup columns by a factor of ProcessorCount. One of the two is wrong;
//    I could not tell which was intended.
//
// 3. Negative collection lengths surface raw framework exceptions rather than CsCheck ones:
//        Gen.Int.Array[-3]      -> OverflowException          Gen.Int.List[-3]   -> ArgumentOutOfRangeException
//        Gen.Char.Array[-5,-1]  -> OverflowException          Gen.String[-5,-1]  -> OverflowException
//    The [start, finish] indexers only check finish < start, which -5,-1 satisfies. The guard wanted is start < 0, not
//    start <= 0, because Array[0] is legitimately an empty array. This is uniform across roughly eight indexers in
//    GenArray, GenArrayUnique, GenArray2D, GenList and GenString, so it may well be a deliberate "caller error fails
//    loudly" choice rather than an oversight. Contrast Gen.Char[""], which was fixed because its own siblings all
//    validated and it alone did not.
//
// 4. Gen.Frequency and Gen.FrequencyConst still accept negative weights, which wrap the total through (uint)i. Given
//    (-1, "a"), (2, "b") the total comes to 1 and the generator always returns "b". Nothing invalid is emitted, only a
//    meaningless distribution. Spec.Action enforces weight >= 1 for the same reason, so a per-element guard would match
//    that convention. A zero total is now rejected, which was the case that silently emitted default(T).
//
// 5. The formatting branch of Reporter.Write in Utils.cs is unreachable from the Spec engines and therefore unpinned by
//    any test. SpecReport.ToString is pure string building over precomputed values, so it cannot throw; everything that
//    depends on the user's printer is rendered earlier, which is why SpecViolation.ToString and PrintTrace are guarded
//    individually. The branch is kept as insurance for other report types, not because it is verified.
//
// 6. GenDateOnly's range indexer uses DateOnly.GetHashCode() as the day number, where DayNumber is the documented
//    property and FromDayNumber is used two lines above. Verified equal at MinValue, MaxValue and a mid date on
//    .NET 11, so it works today, but it depends on an undocumented GetHashCode contract.
//
// 7. Hash.cs handles non-finite values under DecimalPlaces correctly only by accident of .NET's saturating conversions:
//    val - Math.Floor(val) is NaN and (int)NaN is 0, which is exactly what the SignificantFigures branch does
//    deliberately. SignificantFigures needs its explicit guard for a different reason - Math.Log10(Infinity) feeds
//    (int)Math.Floor into an integer overflow and then a garbage Pow10Double index. No test was added because it would
//    be asserting the framework's conversion semantics rather than CsCheck's behaviour.
//
// 8. Spec.cs still says "the limit is eight" in the AtMost and Response summaries. The enforced limit is sixteen, shared
//    across both forms.
//
// 9. Using arg.Length != 0 as a proxy for "this action takes arguments" conflates an argument-less action with one whose
//    argument prints as empty. It survives in ActionNames and Alternatives, where it costs only a label - Set rather
//    than Set(). The Mermaid case, where it dropped arguments from a merged edge label, is fixed. A proper fix needs a
//    hasArguments flag on SpecAction, since ArgCount == 1 cannot distinguish an argument-less action from a
//    single-element domain.
//
// 10. SpecWalk's thread-static tally reuse compares Triggered.Length and Fired.Length but not Unresolved.Length. Safe
//     only because both are sized by requirement count and so cannot diverge.
//
// 11. PCG.Stream returns the canonical representative rather than the constructor argument for stream >= 2^31, because
//     Inc = (stream << 1) | 1 leaves 31 usable bits. This is NOT a bug: PCG's own reference implementation drops the top
//     bit of the sequence selector for the same reason, an LCG's increment having to be odd. I changed this and reverted
//     it. Documenting the limit is the only thing worth considering.
//
// WORTH CONSIDERING
//
// A. One property test over the Gen indexer surface would be worth more than any of the above: for any bounds, either
//    construction throws CsCheckException or every generated value is in range. That single Sample would have caught
//    Gen.TimeSpan[MinValue, MaxValue] (divide by zero), Gen.Char[""] (divide by zero), Frequency with a zero total
//    (emitting default(T)) and the negative-length family, all of which were found one at a time by reading.
//
// B. The comment at Spec.cs:505 claims Validate is "called by every engine". That is currently true at all six call
//    sites, but nothing enforces it. A reflection test enumerating the public engine entry points and asserting each one
//    validates would keep it true as engines are added.
//
// C. Mutation-check multi-site fixes per site, not per fix. Two multi-site changes were half-unpinned when first
//     written: the SampleParallel thread clamp, and the PCG Parse site, where the test only exercised the constructor
//     and reverting the other half left the suite green.
//
// D. Utils.Interleavings uses a labelled continue (loop_i), which needs LangVersion preview. Fine for consumers, since
//    it only affects compiling CsCheck itself, but it does mean the library cannot be built on a stable LangVersion.
//
// PROCESS
//
// E. BOM state differs per file and scripted edits do not preserve it. No BOM: Check.cs, Gen.cs, Spec.cs (Logging.cs
//    starts at namespace). BOM: PCG.cs, Utils.cs, Dbg.cs, Hash.cs, Causal.cs, Timer.cs. Set-Content and
//    File.WriteAllText each default differently, and both silently rewrote the first three bytes during this pass. Any
//    scripted edit needs a git diff afterwards.
//
// F. Two tests fail only when CsCheck_Time is set globally, from wall-clock starvation across the parallel suite rather
//    than from anything they assert: Classify_Table_Survives_A_Failure and SampleModelBasedAsync_Classify.
// ---------------------------------------------------------------------------------------------------------------------