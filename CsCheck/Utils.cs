// Copyright 2024 Anthony Lloyd
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Tests, PublicKey=002400000480000094000000060200000024000052534131000400000100010089f5f142bc30ab84c70e4ccd0b09a684c3d822a99d269cac850f155421fced34048c0e3869a38db5cca81cd8ffcb7469a79422c3a2438a234c7534885471c1cc856ae40461a1ec4a4c5b1d897ba50f70ff486801a482505e0ec506c22da4a6ac5a1d8417e47985aa95caffd180dab750815989d43fcf0a7ee06ce8f1825106d0")]
namespace CsCheck;

using System.Text;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class CsCheckException : Exception
{
    readonly Exception? _exception;
    private CsCheckException() { }
    public CsCheckException(string message) : base(message) { }
    public CsCheckException(string message, Exception? exception)
        : base(exception is null ? message : string.Concat(message, '\n', exception.Message))
    {
        _exception = exception;
    }
    public override string? StackTrace => _exception?.StackTrace;
}

public static partial class Check
{
    static string SampleErrorMessage(string seed, string minT, int shrinks, long skipped, long total)
    {
        const int MAX_LENGTH = 5000;
        if (minT.Length > MAX_LENGTH) minT = $"{minT.AsSpan(0, MAX_LENGTH)} ...";
        return $"Set seed: \"{seed}\" or -e CsCheck_Seed={seed} to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {total:#,0} total).\n{minT}";
    }

    static long ParseEnvironmentVariableToLong(string variable, long defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : long.Parse(value);
    }

    static int ParseEnvironmentVariableToInt(string variable, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : int.Parse(value);
    }

    static double ParseEnvironmentVariableToDouble(string variable, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : double.Parse(value);
    }

    static string? ParseEnvironmentVariableToSeed(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : PCG.Parse(value).ToString();
    }

    static string PrintArray2D(Array a)
    {
        int I = a.GetLength(0), J = a.GetLength(1);
        var sb = new StringBuilder("{");
        for (int i = 0; i < I; i++)
        {
            sb.Append("\n  {");
            for (int j = 0; j < J; j++)
            {
                if (j != 0) sb.Append(", ");
                sb.Append(a.GetValue(i, j));
            }
            sb.Append("},");
        }
        sb.Append("\n}");
        return sb.ToString();
    }

    static bool IsPropertyType(object o)
    {
        var t = o.GetType();
        if (!t.IsGenericType) return false;
        var gt = t.GetGenericTypeDefinition();
        return gt == typeof(KeyValuePair<,>)
            || gt == typeof(Tuple<>)
            || gt == typeof(Tuple<,>)
            || gt == typeof(Tuple<,,>)
            || gt == typeof(Tuple<,,,>)
            || gt == typeof(Tuple<,,,,>)
            || gt == typeof(Tuple<,,,,,>)
            || gt == typeof(Tuple<,,,,,,>)
            || gt == typeof(Tuple<,,,,,,,>);
    }

    static string PrintProperties(object o)
    {
        var sb = new StringBuilder("(");
        var fields = o.GetType().GetProperties();
        sb.Append(Print(fields[0].GetValue(o)));
        for (int i = 1; i < fields.Length; i++)
        {
            sb.Append(", ");
            sb.Append(Print(fields[i].GetValue(o)));
        }
        sb.Append(')');
        return sb.ToString();
    }

    static bool IsFieldType(object o)
    {
        var t = o.GetType();
        if (!t.IsGenericType) return false;
        var gt = t.GetGenericTypeDefinition();
        return gt == typeof(ValueTuple<>)
            || gt == typeof(ValueTuple<,>)
            || gt == typeof(ValueTuple<,,>)
            || gt == typeof(ValueTuple<,,,>)
            || gt == typeof(ValueTuple<,,,,>)
            || gt == typeof(ValueTuple<,,,,,>)
            || gt == typeof(ValueTuple<,,,,,,>)
            || gt == typeof(ValueTuple<,,,,,,,>);
    }

    static string PrintFields(object o)
    {
        var sb = new StringBuilder("(");
        var fields = o.GetType().GetFields();
        sb.Append(Print(fields[0].GetValue(o)));
        for (int i = 1; i < fields.Length; i++)
        {
            sb.Append(", ");
            sb.Append(Print(fields[i].GetValue(o)));
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>Small double string representation.</summary>
    public static string Print(double d)
    {
        static bool TryFraction(double d, out long num, out int den)
        {
            den = 1;
            while (++den < 1000)
            {
                if (d * den > long.MaxValue || d * den < long.MinValue)
                    continue;
                num = (long)Math.Round(d * den);
                if (num != 0 && AreClose(Ulps, (double)num / den, d))
                    return true;
            }
            num = 0;
            den = 0;
            return false;
        }
        static bool TryExponential(double d, out long num, out int exp)
        {
            var isNeg = d < 0;
            d = Math.Abs(d);
            exp = (int)Math.Floor(Math.Log10(d)) - 3;
            var p10 = Math.Pow(10, exp);
            num = (long)Math.Round(d / p10);
            if (AreClose(Ulps, num * p10, d))
            {
                if (isNeg)
                    num = -num;
                while (num % 10 == 0)
                {
                    num /= 10;
                    exp++;
                }
                return true;
            }
            num = 0;
            exp = 0;
            return false;
        }
        var s = d.ToString();
        if (AreClose(Ulps, Math.Round(d), d) && d <= long.MaxValue && d >= long.MinValue)
        {
            var r = ((long)Math.Round(d)).ToString();
            if (r.Length < s.Length)
                s = r;
        }
        if (TryFraction(d, out var num, out var den))
        {
            var fra = $"{num}d/{den}";
            if (fra.Length < s.Length)
                s = fra;
        }
        if (TryExponential(d, out num, out var exp))
        {
            var es = exp == 0 ? num.ToString() : $"{num}E{exp}";
            if (es.Length < s.Length)
                s = es;
        }
        return s;
    }

    /// <summary>Small float string representation.</summary>
    public static string Print(float f)
    {
        static bool TryFraction(float f, out long num, out int den)
        {
            den = 1;
            while (++den < 1000)
            {
                if (f * den > long.MaxValue || f * den < long.MinValue)
                    continue;
                num = (long)Math.Round(f * den);
                if (num != 0 && AreClose(Ulps, (float)num / den, f))
                    return true;
            }
            num = 0;
            den = 0;
            return false;
        }
        static bool TryExponential(float f, out long num, out int exp)
        {
            var isNeg = f < 0;
            f = Math.Abs(f);
            exp = (int)Math.Floor(Math.Log10(f)) - 3;
            var p10 = (float)Math.Pow(10, exp);
            num = (long)Math.Round(f / p10);
            if (AreClose(Ulps, num * p10, f))
            {
                if (isNeg)
                    num = -num;
                while (num % 10 == 0)
                {
                    num /= 10;
                    exp++;
                }
                return true;
            }
            num = 0;
            exp = 0;
            return false;
        }
        var s = f.ToString();
        try
        {
            if (AreClose(Ulps, (float)Math.Round(f), f) && f <= long.MaxValue && f >= long.MinValue)
            {
                var r = ((long)Math.Round(f)).ToString();
                if (r.Length < s.Length)
                    s = r;
            }
        }
        catch { }
        try
        {
            if (TryFraction(f, out var num, out var den))
            {
                var fra = $"{num}f/{den}";
                if (fra.Length < s.Length)
                    s = fra;
            }
        }
        catch { }
        try
        {
            if (TryExponential(f, out var num, out var exp))
            {
                var es = exp == 0 ? num.ToString() : $"{num}E{exp}";
                if (es.Length < s.Length)
                    s = es;
            }
        }
        catch { }
        return s;
    }

    /// <summary>Small decimal string representation.</summary>
    public static string Print(decimal d)
    {
        static bool TryFraction(decimal d, out long num, out int den)
        {
            den = 1;
            while (++den < 1000)
            {
                if (d * den > long.MaxValue || d * den < long.MinValue)
                    continue;
                num = (long)Math.Round(d * den);
                if (num != 0 && AreClose(Ulps, (double)num / den, (double)d))
                    return true;
            }
            num = 0;
            den = 0;
            return false;
        }
        static bool TryExponential(decimal d, out long num, out int exp)
        {
            var isNeg = d < 0;
            d = Math.Abs(d);
            exp = (int)Math.Floor(Math.Log10((double)d)) - 3;
            var p10 = (double)Math.Pow(10, exp);
            num = (long)Math.Round((double)d / p10);
            if (AreClose(Ulps, num * p10, (double)d))
            {
                if (isNeg)
                    num = -num;
                while (num % 10 == 0)
                {
                    num /= 10;
                    exp++;
                }
                return true;
            }
            num = 0;
            exp = 0;
            return false;
        }
        var s = d.ToString();
        try
        {
            if (AreClose(Ulps, (double)Math.Round(d), (double)d) && d <= long.MaxValue && d >= long.MinValue)
            {
                var r = ((long)Math.Round(d)).ToString();
                if (r.Length < s.Length)
                    s = r;
            }
        }
        catch { }
        try
        {
            if (TryFraction(d, out var num, out var den))
            {
                var fra = $"{num}m/{den}";
                if (fra.Length < s.Length)
                    s = fra;
            }
        }
        catch { }
        try
        {
            if (TryExponential(d, out var num, out var exp))
            {
                var es = exp == 0 ? num.ToString() : $"{num}E{exp}";
                if (es.Length < s.Length)
                    s = es;
            }
        }
        catch { }
        return s;
    }

    /// <summary>Default print implementation. Handles most collections and tuple like types.</summary>
    public static string Print<T>(T t) => t switch
    {
        null => "null",
        string s => s,
        Array { Rank: 2 } a => PrintArray2D(a),
        IList { Count: <= 12 } l => "[" + string.Join(", ", l.Cast<object>().Select(Print)) + "]",
        IList l => $"L={l.Count} [{Print(l[0])}, {Print(l[1])}, {Print(l[2])}, {Print(l[3])}, {Print(l[4])}, {Print(l[5])} ... {Print(l[l.Count - 6])}, {Print(l[l.Count - 5])}, {Print(l[l.Count - 4])}, {Print(l[l.Count - 3])}, {Print(l[l.Count - 2])}, {Print(l[l.Count - 1])}]",
        IEnumerable<object> e when !e.Skip(12).Any() => "{" + string.Join(", ", e.Select(Print)) + "}",
        IEnumerable<object> e when !e.Skip(999).Any() => "L=" + e.Count() + " {" + string.Join(", ", e.Select(Print)) + "}",
        IEnumerable<object> e => "L>999 {" + string.Join(", ", e.Take(6).Select(Print)) + " ... }",
        IEnumerable e => Print(e.Cast<object>()),
        T pt when IsPropertyType(pt) => PrintProperties(pt),
        T ft when IsFieldType(ft) => PrintFields(ft),
        double d => Print(d),
        float f => Print(f),
        decimal d => Print(d),
        _ => t.ToString()!,
    };

    /// <summary>Default equal implementation. Handles most collections ordered for IList like or unordered for ICollection based.</summary>
    public static bool Equal<T>(T a, T b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a is IEquatable<T> aieq)
            return aieq.Equals(b);
        if (a is Array aa2 && b is Array ba2 && aa2.Rank == 2)
        {
            int I = aa2.GetLength(0), J = aa2.GetLength(1);
            if (I != ba2.GetLength(0) || J != ba2.GetLength(1)) return false;
            for (int i = 0; i < I; i++)
            {
                for (int j = 0; j < J; j++)
                {
                    if (!Equals(aa2.GetValue(i, j), ba2.GetValue(i, j)))
                        return false;
                }
            }
            return true;
        }
        if (a is IList ail && b is IList bil)
        {
            if (ail.Count != bil.Count) return false;
            for (int i = 0; i < ail.Count; i++)
            {
                if (!ail[i]!.Equals(bil[i]))
                    return false;
            }
            return true;
        }
        if (a.GetType().GetInterface(typeof(IReadOnlyList<>).Name) is not null)
        {
            var e1 = ((IEnumerable)a).GetEnumerator();
            var e2 = ((IEnumerable)b).GetEnumerator();
            while (true)
            {
                var e1MoveNext = e1.MoveNext();
                if (e1MoveNext != e2.MoveNext()) return false;
                if (!e1MoveNext) return true;
                if (!Equal(e1.Current, e2.Current)) return false;
            }
        }
        if (a is ICollection aic && b is ICollection bic)
            return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
        if (a is IEnumerable aie && b is IEnumerable bie)
        {
            var aieo = aie.Cast<object>().ToList();
            var bieo = bie.Cast<object>().ToList();
            return aieo.Count == bieo.Count && !aieo.Except(bieo).Any();
        }
        return a.Equals(b);
    }

    /// <summary>Don't check equality just return true.</summary>
    public static bool EqualSkip<T>(T _, T __) => true;

    /// <summary>Default model equal implementation. Handles most collections ordered when actual is IList like or unordered when actual is ICollection based.</summary>
    public static bool ModelEqual<T, M>(T actual, M model)
    {
        if (actual is null && model is null) return true;
        if (actual is null || model is null) return false;
        if (actual is IList ail && model is IList bil)
        {
            if (ail.Count != bil.Count) return false;
            for (int i = 0; i < ail.Count; i++)
            {
                if (!ail[i]!.Equals(bil[i]))
                    return false;
            }
            return true;
        }
        if (actual.GetType().GetInterface(typeof(IReadOnlyList<>).Name) is not null
              && model.GetType().GetInterface(typeof(IReadOnlyList<>).Name) is not null)
        {
            var e1 = ((IEnumerable)actual).GetEnumerator();
            var e2 = ((IEnumerable)model).GetEnumerator();
            while (true)
            {
                var e1MoveNext = e1.MoveNext();
                if (e1MoveNext != e2.MoveNext()) return false;
                if (!e1MoveNext) return true;
                if (!Equal(e1.Current, e2.Current)) return false;
            }
        }
        if (actual is ICollection aic && model is ICollection bic)
        {
            return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
        }
        if (actual is IEnumerable aie && model is IEnumerable bie)
        {
            var aieo = aie.Cast<object>().ToList();
            var bieo = bie.Cast<object>().ToList();
            return aieo.Count == bieo.Count && !aieo.Except(bieo).Any();
        }
        return actual.Equals(model);
    }

    internal static void Run<T>(T initialState, (string, Action<T>)[] sequencialOperations, (string, Action<T>)[] parallelOperations, int threads, int[]? threadIds = null)
    {
        for (int i = 0; i < sequencialOperations.Length; i++)
            sequencialOperations[i].Item2(initialState);
        Exception? exception = null;
        var opId = -1;
        var runners = new Thread[threads];
        while (--threads >= 0)
        {
            runners[threads] = new Thread(threadId =>
            {
                int i, tid = (int)threadId!;
                while ((i = Interlocked.Increment(ref opId)) < parallelOperations.Length)
                {
                    if (threadIds is not null) threadIds[i] = tid;
                    try { parallelOperations[i].Item2(initialState); }
                    catch (Exception e)
                    {
                        if (exception is null)
                        {
                            exception = e;
                            Interlocked.Exchange(ref opId, parallelOperations.Length);
                        }
                    }
                }
            });
        }
        for (int i = 0; i < runners.Length; i++) runners[i].Start(i);
        for (int i = 0; i < runners.Length; i++) runners[i].Join();
        if (exception is not null) throw exception;
    }

    internal static void RunReplay<T>(T initialState, (string, Action<T>)[] sequencialOperations, (string, Action<T>)[] parallelOperations, int threads, int[] threadIds)
    {
        for (int i = 0; i < sequencialOperations.Length; i++)
            sequencialOperations[i].Item2(initialState);
        Exception? exception = null;
        var runners = new Thread[threads];
        while (--threads >= 0)
        {
            runners[threads] = new Thread(threadId =>
            {
                int opId = -1, i = -1, tid = (int)threadId!;
                while ((i = Interlocked.Increment(ref opId)) < parallelOperations.Length)
                {
                    if (threadIds[i] == tid)
                    {
                        try { parallelOperations[i].Item2(initialState); }
                        catch (Exception e)
                        {
                            if (exception is null)
                            {
                                exception = e;
                                Interlocked.Exchange(ref opId, parallelOperations.Length);
                            }
                        }
                    }
                }
            });
        }
        for (int i = 0; i < runners.Length; i++) runners[i].Start(i);
        for (int i = 0; i < runners.Length; i++) runners[i].Join();
        if (exception is not null) throw exception;
    }

    internal static IEnumerable<T[]> Permutations<T>(int[] threadIds, T[] sequence)
    {
        yield return sequence;
        var next = new List<(int, int[], T[])> { (1, threadIds, sequence) };
        while (next.Count != 0)
        {
            var current = next;
            next = [];
            foreach (var (start, ids, seq) in current)
            {
                for (int i = start; i < ids.Length; i++)
                {
                    int mask = 1 << ids[i];
                    var lastIds = ids;
                    var lastSeq = seq;
                    int u = i;
                    int isMask;
                    while (u-- >= start && (mask & (isMask = 1 << ids[u])) == 0)
                    {
                        mask |= isMask;
                        lastIds = CopySwap(lastIds, u);
                        lastSeq = CopySwap(lastSeq, u);
                        yield return lastSeq;
                        if (u + 2 < ids.Length) next.Add((u + 2, lastIds, lastSeq));
                    }
                }
            }
        }
    }

    static T[] CopySwap<T>(T[] array, int i)
    {
        var copy = new T[array.Length];
        for (int j = 0; j < array.Length; j++)
            copy[j] = array[j];
        (copy[i + 1], copy[i]) = (copy[i], copy[i + 1]);
        return copy;
    }

    /// <summary>Check if two doubles are within the given absolute and relative tolerance.</summary>
    /// <param name="atol">The absolute tolerance.</param>
    /// <param name="rtol">The relative tolerance.</param>
    /// <param name="a">The first double.</param>
    /// <param name="b">The second double.</param>
    /// <returns>True if the values satisfy |a-b| &#8804; atol + rtol max(|a|,|b|).</returns>
    public static bool AreClose(double atol, double rtol, double a, double b)
    {
        var areCloseLhs = Math.Abs(a - b);
        var areCloseRhs = atol + rtol * Math.Max(Math.Abs(a), Math.Abs(b));
        return areCloseLhs <= areCloseRhs;
    }

    /// <summary>Check if two doubles are within the given <paramref name="ulps"/> tolerance.</summary>
    /// <param name="ulps">Units in the last place</param>
    /// <param name="a">The first double.</param>
    /// <param name="b">The second double.</param>
    /// <returns>True if a - b in ulp units &#8804; <paramref name="ulps"/>.</returns>
    public static bool AreClose(int ulps, double a, double b)
    {
        var al = BitConverter.DoubleToInt64Bits(a);
        var bl = BitConverter.DoubleToInt64Bits(b);
        return Math.Abs(bl - al) <= ulps;
    }

    /// <summary>Absolute difference between two doubles in ulps.</summary>
    public static long UlpsBetween(double a, double b)
    {
        var al = BitConverter.DoubleToInt64Bits(a);
        var bl = BitConverter.DoubleToInt64Bits(b);
        return Math.Abs(bl - al);
    }

    [StructLayout(LayoutKind.Explicit)]
    struct FloatConverter
    {
        [FieldOffset(0)] public int I;
        [FieldOffset(0)] public float F;
    }
    /// <summary>Check if two floats are within the given <paramref name="ulps"/> tolerance.</summary>
    /// <param name="ulps">Units in the last place</param>
    /// <param name="a">The first float.</param>
    /// <param name="b">The second float.</param>
    /// <returns>True if a - b in ulp units &#8804; <paramref name="ulps"/>.</returns>
    public static bool AreClose(int ulps, float a, float b)
    {
        var ai = new FloatConverter { F = a }.I;
        var bi = new FloatConverter { F = b }.I;
        return Math.Abs(bi - ai) <= ulps;
    }
}

/// <summary>A median and quartile estimator.</summary>
public sealed class MedianEstimator
{
    /// <summary>The number of sample observations.</summary>
    public int N;
    /// <summary>The number of observations less than or equal to the quantile.</summary>
    public int N0 = 1, N1 = 2, N2 = 3, N3 = 4;
    /// <summary>The minimum or 0th percentile value.</summary>
    public double Q0;
    /// <summary>The first, lower quartile, or 25th percentile value.</summary>
    public double Q1;
    /// <summary>The second quartile, median, or 50th percentile value.</summary>
    public double Q2;
    /// <summary>The third, upper quartile, or 75th percentile value.</summary>
    public double Q3;
    /// <summary>The maximum or 100th percentile value.</summary>
    public double Q4;
    /// <summary>The minimum or 0th percentile value.</summary>
    public double Minimum => Q0;
    /// <summary>The first, lower quartile, or 25th percentile value.</summary>
    public double LowerQuartile => Q1;
    /// <summary>The second quartile, median, or 50th percentile value.</summary>
    public double Median => N == 4 ? (Q2 + Q3) * 0.5 : N == 2 ? (Q1 + Q2) * 0.5 : Q2;
    /// <summary>The third, upper quartile, or 75th percentile value.</summary>
    public double UpperQuartile => Q3;
    /// <summary>The maximum or 100th percentile value.</summary>
    public double Maximum => Q4;

    /// <summary>Add a sample observation.</summary>
    /// <param name="s">Sample observation value.</param>
    public void Add(double s)
    {
        N++;
        if (N > 5)
        {
            if (s <= Q3)
            {
                N3++;
                if (s <= Q2)
                {
                    N2++;
                    if (s <= Q1)
                    {
                        N1++;
                        if (s <= Q0)
                        {
                            if (s == Q0)
                            {
                                N0++;
                            }
                            else
                            {
                                Q0 = s;
                                N0 = 1;
                            }
                        }
                    }
                }
            }
            else if (s > Q4)
            {
                Q4 = s;
            }

            int h;
            double delta, d1, d2;
            s = (N - 1) * 0.25 + 1 - N1;
            if (s >= 1.0 && N2 - N1 > 1)
            {
                h = N2 - N1;
                delta = (Q2 - Q1) / h;
                d1 = PchipDerivative(N1 - N0, (Q1 - Q0) / (N1 - N0), h, delta);
                d2 = PchipDerivative(h, delta, N3 - N2, (Q3 - Q2) / (N3 - N2));
                Q1 += HermiteInterpolationOne(h, delta, d1, d2);
                N1++;
            }
            else if (s <= -1.0 && N1 - N0 > 1)
            {
                h = N1 - N0;
                delta = (Q1 - Q0) / h;
                d1 = PchipDerivativeEnd(h, delta, N2 - N1, (Q2 - Q1) / (N2 - N1));
                d2 = PchipDerivative(h, delta, N2 - N1, (Q2 - Q1) / (N2 - N1));
                Q1 += HermiteInterpolationOne(h, -delta, -d2, -d1);
                N1--;
            }
            s = (N - 1) * 0.50 + 1 - N2;
            if (s >= 1.0 && N3 - N2 > 1)
            {
                h = N3 - N2;
                delta = (Q3 - Q2) / h;
                d1 = PchipDerivative(N2 - N1, (Q2 - Q1) / (N2 - N1), h, delta);
                d2 = PchipDerivative(h, delta, N - N3, (Q4 - Q3) / (N - N3));
                Q2 += HermiteInterpolationOne(h, delta, d1, d2);
                N2++;
            }
            else if (s <= -1.0 && N2 - N1 > 1)
            {
                h = N2 - N1;
                delta = (Q2 - Q1) / h;
                d1 = PchipDerivative(N1 - N0, (Q1 - Q0) / (N1 - N0), h, delta);
                d2 = PchipDerivative(h, delta, N3 - N2, (Q3 - Q2) / (N3 - N2));
                Q2 += HermiteInterpolationOne(h, -delta, -d2, -d1);
                N2--;
            }
            s = (N - 1) * 0.75 + 1 - N3;
            if (s >= 1.0 && N - N3 > 1)
            {
                h = N - N3;
                delta = (Q4 - Q3) / h;
                d1 = PchipDerivative(N3 - N2, (Q3 - Q2) / (N3 - N2), h, delta);
                d2 = PchipDerivativeEnd(h, delta, N3 - N2, (Q3 - Q2) / (N3 - N2));
                Q3 += HermiteInterpolationOne(h, delta, d1, d2);
                N3++;
            }
            else if (s <= -1.0 && N3 - N2 > 1)
            {
                h = N3 - N2;
                delta = (Q3 - Q2) / h;
                d1 = PchipDerivative(N2 - N1, (Q2 - Q1) / (N2 - N1), h, delta);
                d2 = PchipDerivative(h, delta, N - N3, (Q4 - Q3) / (N - N3));
                Q3 += HermiteInterpolationOne(h, -delta, -d2, -d1);
                N3--;
            }
        }
        else if (N == 5)
        {
            if (s > Q4)
            {
                Q0 = Q1;
                Q1 = Q2;
                Q2 = Q3;
                Q3 = Q4;
                Q4 = s;
            }
            else if (s > Q3)
            {
                Q0 = Q1;
                Q1 = Q2;
                Q2 = Q3;
                Q3 = s;
            }
            else if (s > Q2)
            {
                Q0 = Q1;
                Q1 = Q2;
                Q2 = s;
            }
            else if (s > Q1)
            {
                Q0 = Q1;
                Q1 = s;
            }
            else
            {
                Q0 = s;
            }
        }
        else if (N == 4)
        {
            if (s < Q1)
            {
                Q4 = Q3;
                Q3 = Q2;
                Q2 = Q1;
                Q1 = s;
            }
            else if (s < Q2)
            {
                Q4 = Q3;
                Q3 = Q2;
                Q2 = s;
            }
            else if (s < Q3)
            {
                Q4 = Q3;
                Q3 = s;
            }
            else
            {
                Q4 = s;
            }
        }
        else if (N == 3)
        {
            if (s < Q1)
            {
                Q3 = Q2;
                Q2 = Q1;
                Q1 = s;
            }
            else if (s < Q2)
            {
                Q3 = Q2;
                Q2 = s;
            }
            else
            {
                Q3 = s;
            }
        }
        else if (N == 2)
        {
            if (s > Q2)
            {
                Q1 = Q2;
                Q2 = s;
            }
            else
            {
                Q1 = s;
            }
        }
        else
        {
            Q2 = s;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double PchipDerivative(int h1, double delta1, int h2, double delta2)
    {
        return (h1 + h2) * 3 * delta1 * delta2 / ((h1 * 2 + h2) * delta1 + (h2 * 2 + h1) * delta2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double PchipDerivativeEnd(int h1, double delta1, int h2, double delta2)
    {
        double d = (delta1 - delta2) * h1 / (h1 + h2) + delta1;
        return d < 0 ? 0 : d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double HermiteInterpolationOne(int h1, double delta1, double d1, double d2)
    {
        return ((d1 + d2 - delta1 * 2) / h1 + delta1 * 3 - d1 * 2 - d2) / h1 + d1;
    }
}

/// <summary>Median estimate with error. Supports mathematical operators.</summary>
[StructLayout(LayoutKind.Auto)]
public struct MedianEstimate(MedianEstimator e)
{
    public double Median = e.Median, Error = (e.Q3 - e.Q1) * 0.5;
    static double Sqr(double x) => x * x;
    public static MedianEstimate operator -(double a, MedianEstimate e) => new() { Median = a - e.Median, Error = e.Error };
    public static MedianEstimate operator *(MedianEstimate e, double a) => new() { Median = e.Median * a, Error = e.Error * a };
    public static MedianEstimate operator /(MedianEstimate a, MedianEstimate b) => new()
    {
        Median = a.Median / b.Median,
        Error = Math.Sqrt(Sqr(a.Error / a.Median) * Sqr(b.Error / b.Median)) * Math.Abs(a.Median / b.Median),
    };
    public override readonly string ToString() => Math.Min(Math.Max(Median, -99.9), 99.9).ToString("0.0").PadLeft(5)
                                       + " ±" + Math.Min(Error, 99.9).ToString("0.0").PadLeft(4);
}

public sealed class Classifier
{
    readonly ConcurrentDictionary<string, MedianEstimator> estimators = new(StringComparer.Ordinal);
    [ThreadStatic] static MedianEstimator? nextEstimator;
    int nullCount;
    public void Add(string name, long time)
    {
        if (name is not null)
        {
            var estimator = estimators.GetOrAdd(name, nextEstimator ??= new());
            if (ReferenceEquals(estimator, nextEstimator))
                nextEstimator = new();
            lock (estimator)
            {
                estimator.Add(time);
            }
        }
        else
            Interlocked.Increment(ref nullCount);
    }
    public void Print(Action<string> writeLine)
    {
        long total = estimators.Values.Sum(i => i.N);
        foreach (var (summary, s) in estimators.SelectMany(kv =>
                                        {
                                            var a = kv.Key.Split('/');
                                            return Enumerable.Range(1, a.Length - 1).Select(i => string.Join('/', a.Take(i)));
                                        }).ToHashSet(StringComparer.Ordinal)
                                        .Select(summary =>
                                        {
                                            var total = new MedianEstimator();
                                            foreach (var kv in estimators)
                                            {
                                                if (kv.Key.StartsWith(summary, StringComparison.Ordinal))
                                                    total.N += kv.Value.N;
                                            }
                                            return (summary, total);
                                        }).ToList())
            estimators[summary] = s;

        int maxLength = 0;
        foreach (var kv in estimators)
        {
            var a = kv.Key.Split('/');
            var l = (a.Length - 1) * 2 + a[^1].Length;
            if (l > maxLength) maxLength = l;
        }

        var (timeString, timeUnit) = TimeFormat(estimators.Values.Max(i => i.Median));

        var nLength = Math.Max(estimators.Values.Max(i => i.N).ToString("#,##0").Length, 5);
        var lowerLength = Math.Max(timeString(estimators.Values.Max(i => i.LowerQuartile)).Length, 7);
        var medianLength = Math.Max(timeString(estimators.Values.Max(i => i.Median)).Length, 7);
        var upperLength = Math.Max(timeString(estimators.Values.Max(i => i.UpperQuartile)).Length, 7);
        var minimumLength = Math.Max(timeString(estimators.Values.Max(i => i.Minimum)).Length, 7);
        var maximumLength = Math.Max(timeString(estimators.Values.Max(i => i.Maximum)).Length, 7);
        writeLine($"| {new string(' ', maxLength)} | {"Count".PadLeft(nLength)} |       % |   {"Median".PadLeft(medianLength)} |   {"Lower Q".PadLeft(lowerLength)} |   {"Upper Q".PadLeft(upperLength)} |   {"Minimum".PadLeft(minimumLength)} |   {"Maximum".PadLeft(maximumLength)} |");
        writeLine($"|-{new string('-', maxLength)}-|-{new string('-', nLength)}:|--------:|-{new string('-', medianLength)}--:|-{new string('-', lowerLength)}--:|-{new string('-', upperLength)}--:|-{new string('-', minimumLength)}--:|-{new string('-', maximumLength)}--:|");
        foreach (var kv in estimators.OrderByDescending(kv =>
        {
            var a = kv.Key.Split('/');
            var r = new int[a.Length];
            for (int i = 0; i < a.Length - 1; i++)
                r[i] = estimators[string.Join('/', a.Take(i + 1))].N;
            r[^1] = kv.Value.N;
            return (r, kv.Key);
        }, Comparer<(int[], string)>.Create((xb, yb) =>
        {
            var (x, xs) = xb;
            var (y, ys) = yb;
            int c;
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                c = x[i].CompareTo(y[i]);
                if (c != 0)
                    return c;
            }
            c = -x.Length.CompareTo(y.Length);
            if (c != 0)
                return c;
            return -string.CompareOrdinal(xs, ys);
        })))
        {
            var a = kv.Key.Split('/');
            var name = (new string((char)160, 2 * (a.Length - 1)) + a[^1]).PadRight(maxLength);
            var output = $"| {name} | {kv.Value.N.ToString("#,##0").PadLeft(nLength)} | {(float)kv.Value.N / total,7:0.00%} |";
            if (kv.Value.Q2 != 0)
            {
                var median = timeString(kv.Value.Median).PadLeft(medianLength);
                if (kv.Value.N < 5)
                    output += $" {median}{timeUnit} | {new string(' ', lowerLength)}   | {new string(' ', upperLength)}   | {new string(' ', minimumLength)}   | {new string(' ', maximumLength)}   |";
                else
                {
                    var lower = timeString(kv.Value.LowerQuartile).PadLeft(lowerLength);
                    var upper = timeString(kv.Value.UpperQuartile).PadLeft(upperLength);
                    var minimum = timeString(kv.Value.Minimum).PadLeft(minimumLength);
                    var maximum = timeString(kv.Value.Maximum).PadLeft(maximumLength);
                    output += $" {median}{timeUnit} | {lower}{timeUnit} | {upper}{timeUnit} | {minimum}{timeUnit} | {maximum}{timeUnit} |";
                }
            }
            else
                output += $" {new string(' ', medianLength)}   | {new string(' ', lowerLength)}   | {new string(' ', upperLength)}   | {new string(' ', minimumLength)}   | {new string(' ', maximumLength)}   |";
            writeLine(output);
        }
        if (nullCount > 0)
            writeLine($"Null Count: {nullCount:#,##0}");
    }

    static (Func<double, string>, string) TimeFormat(double maxValue) =>
        (maxValue * 1000.0 / Stopwatch.Frequency) switch
        {
            >= 1_000_000 => (d => (d * 1_000 / Stopwatch.Frequency).ToString("#,##0"), "ms"),
            >= 100_000 => (d => (d * 1_000 / Stopwatch.Frequency).ToString("#,##0.0"), "ms"),
            >= 10_000 => (d => (d * 1_000 / Stopwatch.Frequency).ToString("#,##0.00"), "ms"),
            >= 1_000 => (d => (d * 1_000 / Stopwatch.Frequency).ToString("#,##0.000"), "ms"),
            >= 100 => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("#,##0.0"), "μs"),
            >= 10 => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("#,##0.00"), "μs"),
            >= 1 => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("#,##0.000"), "μs"),
            _ => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("#,##0.0000"), "μs"),
        };
}

public static class HashHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetFastModMultiplier(uint divisor)
        => ulong.MaxValue / divisor + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint FastMod(uint value, uint divisor, ulong multiplier)
        => (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPow2(uint value) => (value & (value - 1)) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPow2(int value) => (value & (value - 1)) == 0;
}