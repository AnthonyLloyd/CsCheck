// Copyright 2021 Anthony Lloyd
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
            else if (a.GetType().GetInterface(typeof(IReadOnlyList<>).Name) != null)
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
            else if (actual.GetType().GetInterface(typeof(IReadOnlyList<>).Name) != null
                  && model.GetType().GetInterface(typeof(IReadOnlyList<>).Name) != null)
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
            var s = copy[i];
            copy[i] = copy[i + 1];
            copy[i + 1] = s;
            return copy;
        }
    }

    /// <summary>A median and quartile estimator.</summary>
    public class MedianEstimator
    {
        public int N, N1 = 2, N2 = 3, N3 = 4;
        public double Q0, Q1, Q2, Q3, Q4;
        public double Minimum => Q0;
        public double LowerQuartile => Q1;
        public double Median => Q2;
        public double UpperQuartile => Q3;
        public double Maximum => Q4;
        public void Add(double s)
        {
            if (++N > 5)
            {
                if (s < Q3)
                {
                    N3++;
                    if (s < Q2)
                    {
                        N2++;
                        if (s < Q1)
                        {
                            N1++;
                            if (s < Q0) Q0 = s;
                        }
                    }
                }
                else if (s > Q4) Q4 = s;

                s = 1 - N1 + (N - 1) * 0.25;
                if ((s >= 1.0 && N2 - N1 > 1) || (s <= -1.0 && 1 - N1 < -1))
                {
                    int ds = Math.Sign(s);
                    double q = Q1 + (double)ds / (N2 - 1) * ((N1 - 1 + ds) * (Q2 - Q1) / (N2 - N1) + (N2 - N1 - ds) * (Q1 - Q0) / (N1 - 1));
                    q = Q0 < q && q < Q2 ? q
                      : ds == 1 ? Q1 + (Q2 - Q1) / (N2 - N1)
                      : Q1 - (Q0 - Q1) / (1 - N1);
                    N1 += ds;
                    Q1 = q;
                }
                s = 1 - N2 + (N - 1) * 0.50;
                if ((s >= 1.0 && N3 - N2 > 1) || (s <= -1.0 && N1 - N2 < -1))
                {
                    int ds = Math.Sign(s);
                    double q = Q2 + (double)ds / (N3 - N1) * ((N2 - N1 + ds) * (Q3 - Q2) / (N3 - N2) + (N3 - N2 - ds) * (Q2 - Q1) / (N2 - N1));
                    q = Q1 < q && q < Q3 ? q
                      : ds == 1 ? Q2 + (Q3 - Q2) / (N3 - N2)
                      : Q2 - (Q1 - Q2) / (N1 - N2);
                    N2 += ds;
                    Q2 = q;
                }
                s = 1 - N3 + (N - 1) * 0.75;
                if ((s >= 1.0 && N - N3 > 1) || (s <= -1.0 && N2 - N3 < -1))
                {
                    int ds = Math.Sign(s);
                    double q = Q3 + (double)ds / (N - N2) * ((N3 - N2 + ds) * (Q4 - Q3) / (N - N3) + (N - N3 - ds) * (Q3 - Q2) / (N3 - N2));
                    q = Q2 < q && q < Q4 ? q
                      : ds == 1 ? Q3 + (Q4 - Q3) / (N - N3)
                      : Q3 - (Q2 - Q3) / (N2 - N3);
                    N3 += ds;
                    Q3 = q;
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
    }

    /// <summary>Median estimate with error. Supports mathematical operators.</summary>
    public struct MedianEstimate
    {
        public double Median, Error;
        public MedianEstimate(MedianEstimator e)
        {
            Median = e.Median;
            Error = (e.UpperQuartile - e.LowerQuartile) * 0.5;
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