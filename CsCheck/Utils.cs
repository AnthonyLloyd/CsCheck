﻿// Copyright 2021 Anthony Lloyd
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
using System.Threading;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Tests, PublicKey=002400000480000094000000060200000024000052534131000400000100010089f5f142bc30ab84c70e4ccd0b09a684c3d822a99d269cac850f155421fced34048c0e3869a38db5cca81cd8ffcb7469a79422c3a2438a234c7534885471c1cc856ae40461a1ec4a4c5b1d897ba50f70ff486801a482505e0ec506c22da4a6ac5a1d8417e47985aa95caffd180dab750815989d43fcf0a7ee06ce8f1825106d0")]
namespace CsCheck
{
    internal class ThreadUtils
    {
        static readonly int[] DummyArray = new int[Check.MAX_CONCURRENT_OPERATIONS];
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
            array = (T[])array.Clone();
            var s = array[i];
            array[i] = array[i + 1];
            array[i + 1] = s;
            return array;
        }
    }

    public class MedianEstimator
    {
        int N, n2 = 2, n3 = 3, n4 = 4;
        double q1, q2, q3, q4, q5;
        public double Minimum => q1;
        public double LowerQuartile => q2;
        public double Median => q3;
        public double UpperQuartile => q4;
        public double Maximum => q5;
        public void Add(double s)
        {
            switch (++N)
            {
                case 1:
                    q1 = s;
                    return;
                case 2:
                    q2 = s;
                    return;
                case 3:
                    q3 = s;
                    return;
                case 4:
                    q4 = s;
                    return;
                case 5:
                    var a = new[] { q1, q2, q3, q4, s };
                    Array.Sort(a);
                    q1 = a[0];
                    q2 = a[1];
                    q3 = a[2];
                    q4 = a[3];
                    q5 = a[4];
                    return;
                default:
                    if (s < q1) q1 = s;
                    if (s < q2) n2++;
                    if (s < q3) n3++;
                    if (s < q4) n4++;
                    if (s > q5) q5 = s;
                    Adjust(0.25, 1, ref n2, n3, q1, ref q2, q3);
                    Adjust(0.50, n2, ref n3, n4, q2, ref q3, q4);
                    Adjust(0.75, n3, ref n4, N, q3, ref q4, q5);
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Adjust(double p, int n1, ref int n2, int n3, double q1, ref double q2, double q3)
        {
            double d = 1 - n2 + (N - 1) * p;
            if ((d >= 1.0 && n3 - n2 > 1) || (d <= -1.0 && n1 - n2 < -1))
            {
                int ds = Math.Sign(d);
                double q = q2 + (double)ds / (n3 - n1) * ((n2 - n1 + ds) * (q3 - q2) / (n3 - n2) + (n3 - n2 - ds) * (q2 - q1) / (n2 - n1));
                q = q1 < q && q < q3 ? q :
                    ds == 1 ? q2 + (q3 - q2) / (n3 - n2) :
                    q2 - (q1 - q2) / (n1 - n2);
                n2 += ds;
                q2 = q;
            }
        }
    }
}