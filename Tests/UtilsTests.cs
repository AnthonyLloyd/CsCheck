using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CsCheck;
using Xunit;

namespace Tests
{

    public class ThreadStatsTests
    {
        static void Test(int[] ids, IEnumerable<int[]> expected)
        {
            var seq = new int[ids.Length];
            Array.Copy(ids, seq, ids.Length);
            Assert.Equal(Check.Permutations(ids, seq), expected, IntArrayComparer.Default);
        }

        [Fact]
        public void Permutations_11()
        {
            Test(new int[] { 1, 1 }, new[] {
                new int[] { 1, 1 },
            });
        }

        [Fact]
        public void Permutations_12()
        {
            Test(new int[] { 1, 2 }, new[] {
                new int[] { 1, 2 },
                new int[] { 2, 1 },
            });
        }

        [Fact]
        public void Permutations_112()
        {
            Test(new int[] { 1, 1, 2 }, new[] {
                new int[] { 1, 1, 2 },
                new int[] { 1, 2, 1 },
            });
        }

        [Fact]
        public void Permutations_121()
        {
            Test(new int[] { 1, 2, 1 }, new[] {
                new int[] { 1, 2, 1 },
                new int[] { 2, 1, 1 },
                new int[] { 1, 1, 2 },
            });
        }

        [Fact]
        public void Permutations_123()
        {
            Test(new int[] { 1, 2, 3 }, new[] {
                new int[] { 1, 2, 3 },
                new int[] { 2, 1, 3 },
                new int[] { 1, 3, 2 },
                new int[] { 3, 1, 2 },
                new int[] { 2, 3, 1 },
                new int[] { 3, 2, 1 },
            });
        }

        [Fact]
        public void Permutations_1212()
        {
            Test(new int[] { 1, 2, 1, 2 }, new[] {
                new int[] { 1, 2, 1, 2 },
                new int[] { 2, 1, 1, 2 },
                new int[] { 1, 1, 2, 2 },
                new int[] { 1, 2, 2, 1 },
                new int[] { 2, 1, 2, 1 },
            });
        }

        [Fact]
        public void Permutations_1231()
        {
            Test(new int[] { 1, 2, 3, 1 }, new[] {
                new int[] { 1, 2, 3, 1 },
                new int[] { 2, 1, 3, 1 },
                new int[] { 1, 3, 2, 1 },
                new int[] { 3, 1, 2, 1 },
                new int[] { 1, 2, 1, 3 },
                new int[] { 1, 1, 2, 3 },
                new int[] { 2, 3, 1, 1 },
                new int[] { 2, 1, 1, 3 },
                new int[] { 1, 3, 1, 2 },
                new int[] { 3, 2, 1, 1 },
                new int[] { 3, 1, 1, 2 },
                new int[] { 1, 1, 3, 2 },
            });
        }

        [Fact]
        public void Permutations_1232()
        {
            Test(new int[] { 1, 2, 3, 2 }, new[] {
                new int[] { 1, 2, 3, 2 },
                new int[] { 2, 1, 3, 2 },
                new int[] { 1, 3, 2, 2 },
                new int[] { 3, 1, 2, 2 },
                new int[] { 1, 2, 2, 3 },
                new int[] { 2, 3, 1, 2 },
                new int[] { 2, 1, 2, 3 },
                new int[] { 2, 2, 1, 3 },
                new int[] { 3, 2, 1, 2 },
                new int[] { 2, 3, 2, 1 },
                new int[] { 2, 2, 3, 1 },
                new int[] { 3, 2, 2, 1 },
            });
        }

        [Fact]
        public void Permutations_Should_Be_Unique()
        {
            Gen.Int[0, 5].Array[0, 10]
            .Sample(a =>
            {
                var a2 = new int[a.Length];
                Array.Copy(a, a2, a.Length);
                var ps = Check.Permutations(a, a2).ToList();
                var ss = new HashSet<int[]>(ps, IntArrayComparer.Default);
                Assert.Equal(ss.Count, ps.Count);
            });
        }
    }

    public class IntArrayComparer : IEqualityComparer<int[]>, IComparer<int[]>
    {
        public readonly static IntArrayComparer Default = new();
        public int Compare(int[] x, int[] y)
        {
            for (int i = 0; i < x.Length; i++)
            {
                int c = x[i].CompareTo(y[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        public bool Equals(int[] x, int[] y)
        {
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return false;
            return true;
        }

        public int GetHashCode([DisallowNull] int[] a)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (int i in a)
                    hash = (hash * 16777619) ^ i;
                return hash;
            }
        }
    }

    public class MedianEstimatorTests
    {
        [Fact]
        public void MedianEstimator()
        {
            Gen.Double[-1000.0, 1000.0].Array[5, 100]
            .Sample(values =>
            {
                var expected = new P2QuantileEstimator(0.5);
                foreach (var v in values) expected.AddValue(v);
                var actual = new MedianEstimator();
                foreach (var v in values) actual.Add(v);
                Assert.Equal(expected.GetQuantile(), actual.Median);
            });
        }
    }


    public class P2QuantileEstimator
    {
        private readonly double p;
        private readonly int[] n = new int[5]; // marker positions
        private readonly double[] ns = new double[5]; // desired marker positions
        private readonly double[] dns = new double[5];
        private readonly double[] q = new double[5]; // marker heights
        private int count;

        public P2QuantileEstimator(double probability)
        {
            p = probability;
        }

        public void AddValue(double x)
        {
            if (count < 5)
            {
                q[count++] = x;
                if (count == 5)
                {
                    Array.Sort(q);

                    for (int i = 0; i < 5; i++)
                        n[i] = i;

                    ns[0] = 0;
                    ns[1] = 2 * p;
                    ns[2] = 4 * p;
                    ns[3] = 2 + 2 * p;
                    ns[4] = 4;

                    dns[0] = 0;
                    dns[1] = p / 2;
                    dns[2] = p;
                    dns[3] = (1 + p) / 2;
                    dns[4] = 1;
                }

                return;
            }

            int k;
            if (x < q[0])
            {
                q[0] = x;
                k = 0;
            }
            else if (x < q[1])
                k = 0;
            else if (x < q[2])
                k = 1;
            else if (x < q[3])
                k = 2;
            else if (x < q[4])
                k = 3;
            else
            {
                q[4] = x;
                k = 3;
            }

            for (int i = k + 1; i < 5; i++)
                n[i]++;
            for (int i = 0; i < 5; i++)
                ns[i] += dns[i];

            for (int i = 1; i <= 3; i++)
            {
                double d = ns[i] - n[i];
                if (d >= 1 && n[i + 1] - n[i] > 1 || d <= -1 && n[i - 1] - n[i] < -1)
                {
                    int dInt = Math.Sign(d);
                    double qs = Parabolic(i, dInt);
                    if (q[i - 1] < qs && qs < q[i + 1])
                        q[i] = qs;
                    else
                        q[i] = Linear(i, dInt);
                    n[i] += dInt;
                }
            }

            count++;
        }

        private double Parabolic(int i, double d)
        {
            return q[i] + d / (n[i + 1] - n[i - 1]) * (
                (n[i] - n[i - 1] + d) * (q[i + 1] - q[i]) / (n[i + 1] - n[i]) +
                (n[i + 1] - n[i] - d) * (q[i] - q[i - 1]) / (n[i] - n[i - 1])
            );
        }

        private double Linear(int i, int d)
        {
            return q[i] + d * (q[i + d] - q[i]) / (n[i + d] - n[i]);
        }

        public double GetQuantile()
        {
            if (count <= 5)
            {
                Array.Sort(q, 0, count);
                int index = (int)Math.Round((count - 1) * p);
                return q[index];
            }

            return q[2];
        }
    }
}
