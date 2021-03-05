using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Microsoft.Collections.Extensions;
using Xunit;

namespace Tests
{
    public class CheckTests
    {
        readonly Action<string> writeLine;
        public CheckTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        static void Assert_Commutative<T, R>(Gen<T> gen, Func<T, T, R> operation)
        {
            Gen.Select(gen, gen)
            .Sample(t => Assert.Equal(operation(t.V0, t.V1), operation(t.V1, t.V0)));
        }

        [Fact]
        public void Sample_Addition_Is_Commutative()
        {
            Assert_Commutative(Gen.Byte, (x, y) => x + y);
            Assert_Commutative(Gen.SByte, (x, y) => x + y);
            Assert_Commutative(Gen.UShort, (x, y) => x + y);
            Assert_Commutative(Gen.Short, (x, y) => x + y);
            Assert_Commutative(Gen.UInt, (x, y) => x + y);
            Assert_Commutative(Gen.Int, (x, y) => x + y);
            Assert_Commutative(Gen.ULong, (x, y) => x + y);
            Assert_Commutative(Gen.Long, (x, y) => x + y);
            Assert_Commutative(Gen.Single, (x, y) => x + y);
            Assert_Commutative(Gen.Double, (x, y) => x + y);
        }

        [Fact]
        public void Sample_Multiplication_Is_Commutative()
        {
            Assert_Commutative(Gen.Byte, (x, y) => x * y);
            Assert_Commutative(Gen.SByte, (x, y) => x * y);
            Assert_Commutative(Gen.UShort, (x, y) => x * y);
            Assert_Commutative(Gen.Short, (x, y) => x * y);
            Assert_Commutative(Gen.UInt, (x, y) => x * y);
            Assert_Commutative(Gen.Int, (x, y) => x * y);
            Assert_Commutative(Gen.ULong, (x, y) => x * y);
            Assert_Commutative(Gen.Long, (x, y) => x * y);
            Assert_Commutative(Gen.Single, (x, y) => x * y);
            Assert_Commutative(Gen.Double, (x, y) => x * y);
        }

        static void Assert_Associative<T>(Gen<T> gen, Func<T, T, T> operation)
        {
            Gen.Select(gen, gen, gen)
            .Sample(t => Assert.Equal(operation(t.V0, operation(t.V1, t.V2)),
                                      operation(operation(t.V0, t.V1), t.V2)));
        }

        [Fact]
        public void Sample_Addition_Is_Associative()
        {
            Assert_Associative(Gen.UInt, (x, y) => x + y);
            Assert_Associative(Gen.Int, (x, y) => x + y);
            Assert_Associative(Gen.ULong, (x, y) => x + y);
            Assert_Associative(Gen.Long, (x, y) => x + y);
        }

        [Fact]
        public void Sample_Multiplication_Is_Associative()
        {
            Assert_Associative(Gen.UInt, (x, y) => x * y);
            Assert_Associative(Gen.Int, (x, y) => x * y);
            Assert_Associative(Gen.ULong, (x, y) => x * y);
            Assert_Associative(Gen.Long, (x, y) => x * y);
        }

        static double[,] MulIJK(double[,] a, double[,] b)
        {
            int I = a.GetLength(0), J = a.GetLength(1), K = b.GetLength(1);
            var c = new double[I, K];
            for (int i = 0; i < I; i++)
                for (int j = 0; j < J; j++)
                    for (int k = 0; k < K; k++)
                        c[i, k] += a[i, j] * b[j, k];
            return c;
        }

        static double[,] MulIKJ(double[,] a, double[,] b)
        {
            int I = a.GetLength(0), J = a.GetLength(1), K = b.GetLength(1);
            var c = new double[I, K];
            for (int i = 0; i < I; i++)
                for (int k = 0; k < K; k++)
                {
                    double t = 0.0;
                    for (int j = 0; j < J; j++)
                        t += a[i, j] * b[j, k];
                    c[i, k] = t;
                }
            return c;
        }

        [Fact]
        public void Faster_Matrix_Multiply_Fixed()
        {
            int I = 30, J = 37, K = 29;
            var rand = new Random(42);
            var a = new double[I, J];
            for (int i = 0; i < I; i++)
                for (int j = 0; j < J; j++)
                    a[i, j] = rand.NextDouble();
            var b = new double[J, K];
            for (int j = 0; j < J; j++)
                for (int k = 0; k < K; k++)
                    b[j, k] = rand.NextDouble();
            Check.Faster(
                () => MulIKJ(a, b),
                () => MulIJK(a, b),
                Assert.Equal
            )
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Matrix_Multiply_Range()
        {
            var genDim = Gen.Int[5, 30];
            var genArray = Gen.Double.Unit.Array2D;
            Gen.SelectMany(genDim, genDim, genDim, (i, j, k) =>
                Gen.Select(genArray[i, j], genArray[j, k])
            )
            .Faster(
                t => MulIKJ(t.V0, t.V1),
                t => MulIJK(t.V0, t.V1),
                Assert.Equal
            )
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Linq_Fixed()
        {
            var data = new byte[1000];
            new Random(42).NextBytes(data);
            Check.Faster(
                () => data.Aggregate(0.0, (t, b) => t + b),
                () => data.Select(i => (double)i).Sum()
            )
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Linq_Random()
        {
            Gen.Byte.Array[100, 1000]
            .Faster(
                data => data.Aggregate(0.0, (t, b) => t + b),
                data => data.Select(i => (double)i).Sum()
            )
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Linq_Imperative_Random()
        {
            Gen.Byte.Array[100, 1000]
            .Faster(
                data =>
                {
                    double s = 0.0;
                    foreach (var b in data) s += b;
                    return s;
                },
                data => data.Aggregate(0.0, (t, b) => t + b)
            )
            .Output(writeLine);
        }

        [Fact]
        public void Faster_DictionarySlim_Counter()
        {
            Gen.Byte.Array
            .Faster(
                t =>
                {
                    var d = new DictionarySlim<byte, int>();
                    for (int i = 0; i < t.Length; i++)
                        d.GetOrAddValueRef(t[i])++;
                    return d.Count;
                },
                t =>
                {
                    var d = new Dictionary<byte, int>();
                    for (int i = 0; i < t.Length; i++)
                    {
                        var k = t[i];
                        d.TryGetValue(k, out var v);
                        d[k] = v + 1;
                    }
                    return d.Count;
                }
            )
            .Output(writeLine);
        }

        [Fact]
        public void Multithreading_DictionarySlim()
        {
            var d = new DictionarySlim<int, int>();
            Check.Sample(
                Gen.Int[1, 10],
                i =>
                {
                    ref var v = ref d.GetOrAddValueRef(i);
                    v = 1 - v;
                },
                Gen.Int[1, 10],
                i =>
                {
                    d.TryGetValue(i, out var v);
                    Assert.True(v == 0 || v == 1);
                }
            );
        }

        [Fact]
        public void Multithreading_ConcurrentDictionary()
        {
            var d = new ConcurrentDictionary<int, int>();
            Check.Sample(
                Gen.Int[1, 10],
                i => d.AddOrUpdate(i, 0, (_, v) => 1 - v),
                Gen.Int[1, 10],
                i =>
                {
                    d.TryGetValue(i, out var v);
                    Assert.True(v == 0 || v == 1);
                },
                Gen.Const(0),
                _ => Assert.True(d.Count <= 10)
            );
        }

        [Fact]
        public void Multithreading_ConcurrentBag()
        {
            Gen.Int[5, 40].SampleOne(n =>
            {
                var b = new ConcurrentBag<int>();
                Check.Sample(
                    Gen.Int[1, n],
                    b.Add,
                    Gen.Const(0),
                    _ =>
                    {
                        b.TryTake(out int v);
                        Assert.True(v >= 0 && v <= n);
                    },
                    Gen.Const(0),
                    _ =>
                    {
                        b.TryPeek(out int v);
                        Assert.True(v >= 0 && v <= n);
                    },
                    Gen.Const(0),
                    _ => b.Clear()
                );
            });
        }

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

        [Fact]
        public void ConcurrentBag_ModelBased()
        {
            // Model-based testing of a ConcurrentBag using a List as the model.
            // The operations are run in a random sequence on an initial random ConcurrencyBag checking that the bag and model are always equal.
            // If not the failing sequence will be shrunk down to the shortest and simplest and simplest initial bag.
            Gen.Int.List.Select(l => (new ConcurrentBag<int>(l), l))
            .SampleModelBased(
                // Equality check of bag vs list.
                equal: (bag, list) => bag.OrderBy(i => i).SequenceEqual(list.OrderBy(i => i)),
                // Add operation - Gen used to create the data required and this is turned into an Action on the bag and list.
                Gen.Int.Select<int, Action<ConcurrentBag<int>, List<int>>>(i => (bag, list) =>
                {
                    bag.Add(i);
                    list.Add(i);
                }),
                // TryTake operation - An example of an operation that doesn't need any data. This operation also has a post assert.
                Gen.Const<Action<ConcurrentBag<int>, List<int>>>((bag, list) =>
                {
                    Assert.Equal(bag.TryTake(out var i), list.Remove(i));
                })
                // Other operations ...
            );
        }

        [Fact]
        public void ConcurrentBag_Concurrent()
        {
            // Concurrency testing of a ConcurrentBag.
            // A random list of operations are run in parallel. The result is compared against the result of the possible sequential permutations.
            // At least one of these permutations result must be equal to it for the concurrency to have been linearized successfully.
            // If not the failing list will be shrunk down to the shortest and simplest and simplest initial bag.
            Gen.Int.List.Select(l => new ConcurrentBag<int>(l))
            .SampleConcurrent(new SampleOptions<ConcurrentBag<int>> { Threads = 10 },
                // Equality check of bag vs bag.
                equal: (bag1, bag2) => bag1.OrderBy(i => i).SequenceEqual(bag2.OrderBy(i => i)),
                // Add operation - Gen used to create the data required and this is turned into an Action on the bag.
                Gen.Int.Select<int, Action<ConcurrentBag<int>>>(i => bag =>
                {
                    bag.Add(i);
                }),
                // TryTake operation - An example of an operation that doesn't need any data.
                Gen.Const<Action<ConcurrentBag<int>>>(bag =>
                {
                    bag.TryTake(out var i);
                })
                // Other operations ...
            );
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