// Copyright 2022 Anthony Lloyd
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
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests, PublicKey=002400000480000094000000060200000024000052534131000400000100010089f5f142bc30ab84c70e4ccd0b09a684c3d822a99d269cac850f155421fced34048c0e3869a38db5cca81cd8ffcb7469a79422c3a2438a234c7534885471c1cc856ae40461a1ec4a4c5b1d897ba50f70ff486801a482505e0ec506c22da4a6ac5a1d8417e47985aa95caffd180dab750815989d43fcf0a7ee06ce8f1825106d0")]
namespace CsCheck
{
    public partial class Check
    {
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
            sb.Append(")");
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
                || gt == typeof(ValueTuple<,,,,>)
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
            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>Default print implementation. Handles most collections and tuple like types.</summary>
        public static string Print<T>(T t) => t switch
        {
            null => "null",
            string s => s,
            Array { Rank: 2 } a => PrintArray2D(a),
            IList { Count: <= 12 } l => "[" + string.Join(", ", l.Cast<object>().Select(Print)) + "]",
            IList l => $"L={l.Count} [{Print(l[0])}, {Print(l[1])}, {Print(l[2])} ... {Print(l[l.Count - 2])}, {Print(l[l.Count - 1])}]",
            IEnumerable<object> e when e.Take(12).Count() <= 12 => "{" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e when e.Take(999).Count() <= 999 => "L=" + e.Count() + " {" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e => "L>999 {" + string.Join(", ", e.Take(6).Select(Print)) + " ... }",
            IEnumerable e => Print(e.Cast<object>()),
            T pt when IsPropertyType(pt) => PrintProperties(pt),
            T ft when IsFieldType(ft) => PrintFields(ft),
            _ => t.ToString(),
        };

        /// <summary>Default equal implementation. Handles most collections ordered for IList like or unordered for ICollection based.</summary>
        public static bool Equal<T>(T a, T b)
        {
            if (a is IEquatable<T> aieq) return aieq.Equals(b);
            else if (a is Array aa2 && b is Array ba2 && aa2.Rank == 2)
            {
                int I = aa2.GetLength(0), J = aa2.GetLength(1);
                if (I != ba2.GetLength(0) || J != ba2.GetLength(1)) return false;
                for (int i = 0; i < I; i++)
                    for (int j = 0; j < J; j++)
                        if (!aa2.GetValue(i, j).Equals(ba2.GetValue(i, j)))
                            return false;
                return true;
            }
            else if (a is IList ail && b is IList bil)
            {
                if (ail.Count != bil.Count) return false;
                for (int i = 0; i < ail.Count; i++)
                    if (!ail[i].Equals(bil[i]))
                        return false;
                return true;
            }
            else if (a.GetType().GetInterface(typeof(IReadOnlyList<>).Name) is not null)
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
            else if (a is ICollection aic && b is ICollection bic)
            {
                return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
            }
            else if (a is IEnumerable aie && b is IEnumerable bie)
            {
                var aieo = aie.Cast<object>().ToList();
                var bieo = bie.Cast<object>().ToList();
                return aieo.Count == bieo.Count && !aieo.Except(bieo).Any();
            }
            return a.Equals(b);
        }

        /// <summary>Default model equal implementation. Handles most collections ordered when actual is IList like or unordered when actual is ICollection based.</summary>
        public static bool ModelEqual<T, M>(T actual, M model)
        {
            if (actual is IList ail && model is IList bil)
            {
                if (ail.Count != bil.Count) return false;
                for (int i = 0; i < ail.Count; i++)
                    if (!ail[i].Equals(bil[i]))
                        return false;
                return true;
            }
            else if (actual.GetType().GetInterface(typeof(IReadOnlyList<>).Name) is not null
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
            else if (actual is ICollection aic && model is ICollection bic)
            {
                return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
            }
            else if (actual is IEnumerable aie && model is IEnumerable bie)
            {
                var aieo = aie.Cast<object>().ToList();
                var bieo = bie.Cast<object>().ToList();
                return aieo.Count == bieo.Count && !aieo.Except(bieo).Any();
            }
            return actual.Equals(model);
        }

        static readonly int[] DummyArray = new int[MAX_CONCURRENT_OPERATIONS];
        internal static void Run<T>(T concurrentState, (string, Action<T>)[] operations, int threads, int[] threadIds = null)
        {
            if (threadIds is null) threadIds = DummyArray;
            Exception exception = null;
            var opId = -1;
            var runners = new Thread[threads];
            while (--threads >= 0)
            {
                runners[threads] = new Thread(threadId =>
                {
                    int i, tid = (int)threadId;
                    while ((i = Interlocked.Increment(ref opId)) < operations.Length)
                    {
                        threadIds[i] = tid;
                        try { operations[i].Item2(concurrentState); }
                        catch (Exception e)
                        {
                            if (exception is null)
                            {
                                exception = e;
                                Interlocked.Exchange(ref opId, operations.Length);
                            }
                        }
                    }
                });
            }
            for (int i = 0; i < runners.Length; i++) runners[i].Start(i);
            for (int i = 0; i < runners.Length; i++) runners[i].Join();
            if (exception is not null) throw exception;
        }

        internal static void RunReplay<T>(T concurrentState, (string, Action<T>)[] operations, int threads, int[] threadIds)
        {
            Exception exception = null;
            var runners = new Thread[threads];
            while (--threads >= 0)
            {
                runners[threads] = new Thread(threadId =>
                {
                    int opId = -1, i = -1, tid = (int)threadId;
                    while ((i = Interlocked.Increment(ref opId)) < operations.Length)
                    {
                        if (threadIds[i] == tid)
                        {
                            try { operations[i].Item2(concurrentState); }
                            catch (Exception e)
                            {
                                if (exception is null)
                                {
                                    exception = e;
                                    Interlocked.Exchange(ref opId, operations.Length);
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
                next = new();
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
    }

    /// <summary>A median and quartile estimator.</summary>
    public class MedianEstimator
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
        public double Median => Q2;
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
                else if (s > Q4) Q4 = s;
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
                else Q0 = s;
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
                else Q4 = s;
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
                else Q3 = s;
            }
            else if (N == 2)
            {
                if (s > Q2)
                {
                    Q1 = Q2;
                    Q2 = s;
                }
                else Q1 = s;
            }
            else Q2 = s;
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
    public struct MedianEstimate
    {
        public double Median, Error;
        public MedianEstimate(MedianEstimator e)
        {
            Median = e.Median;
            Error = (e.Q3 - e.Q1) * 0.5;
        }
        static double Sqr(double x) => x * x;
        public static MedianEstimate operator -(double a, MedianEstimate e) => new() { Median = a - e.Median, Error = e.Error };
        public static MedianEstimate operator *(MedianEstimate e, double a) => new() { Median = e.Median * a, Error = e.Error * a };
        public static MedianEstimate operator /(MedianEstimate a, MedianEstimate b) => new()
        {
            Median = a.Median / b.Median,
            Error = Math.Sqrt(Sqr(a.Error / a.Median) * Sqr(b.Error / b.Median)) * Math.Abs(a.Median / b.Median)
        };
        public override string ToString() => Math.Min(Math.Max(Median, -99.9), 99.9).ToString("0.0").PadLeft(5)
                                           + " ±" + Math.Min(Error, 99.9).ToString("0.0").PadLeft(4);
    }
}