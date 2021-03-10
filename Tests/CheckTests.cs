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
        public void Faster_Linq_Random()
        {
            Gen.Byte.Array[100, 1000]
            .Faster(
                data => data.Aggregate(0.0, (t, b) => t + b),
                data => data.Select(i => (double)i).Sum(),
                sigma: 50
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
        public void SampleModelBased_ConcurrentBag()
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
        public void SampleConcurrent_ConcurrentBag()
        {
            // Concurrency testing of a ConcurrentBag.
            // A random list of operations are run in parallel. The result is compared against the result of the possible sequential permutations.
            // At least one of these permutations result must be equal to it for the concurrency to have been linearized successfully.
            // If not the failing list will be shrunk down to the shortest and simplest and simplest initial bag.
            Gen.Int.List[0, 5].Select(l => new ConcurrentBag<int>(l))
            .SampleConcurrent(
                // Equality check of bag vs bag.
                equal: (actual, pos) => actual.OrderBy(i => i).SequenceEqual(pos.OrderBy(i => i)),
                // Add operation - Gen used to create the data required and this is turned into an Action on the bag.
                Gen.Int.Select<int, (string, Action<ConcurrentBag<int>>)>(i => ($"Add({i})", bag => bag.Add(i))),
                // TryTake operation - An example of an operation that doesn't need any data.
                Gen.Const<(string, Action<ConcurrentBag<int>>)>(("TryTake()", bag => bag.TryTake(out _)))
                // Other operations ...
            );
        }

        //[Fact]
        //public void SampleConcurrent_List()
        //{
        //    Gen.Int.List
        //    .SampleConcurrent(new SampleOptions<List<int>> { Size = 1_000_000 },
        //        // Equality check of bag vs bag.
        //        equal: (actual, pos) => actual.OrderBy(i => i).SequenceEqual(pos.OrderBy(i => i)),
        //        // Add operation - Gen used to create the data required and this is turned into an Action on the bag.
        //        Gen.Int.Select<int, (string, Action<List<int>>)>(i => ($"Add({i})", list => list.Add(i)))
        //        // TryTake operation - An example of an operation that doesn't need any data.
        //        //Gen.Const<(string, Action<List<int>>)>(("Remove()", list => list.RemoveAt(0)))
        //    // Other operations ...
        //    );
        //}

        [Fact]
        public void SampleConcurrent_ConcurrentDictionary()
        {
            Gen.Dictionary(Gen.Int[0, 100], Gen.Byte)[0, 10].Select(l => new ConcurrentDictionary<int, byte>(l))
            .SampleConcurrent(

                equal: (actual, pos) => actual.OrderBy(i => i.Key).SequenceEqual(pos.OrderBy(i => i.Key)),

                Gen.Int[0, 100].Select(Gen.Byte).Select<(int, byte), (string, Action<ConcurrentDictionary<int, byte>>)>(t =>
                      ($"d[{t.Item1}] = {t.Item2}", d => d[t.Item1] = t.Item2)
                ),

                Gen.Int[0, 100].Select<int, (string, Action<ConcurrentDictionary<int, byte>>)>(i =>
                     ($"TryRemove({i})", d => d.TryRemove(i, out _))
                )
            );
        }

        [Fact]
        public void SampleConcurrent_ConcurrentQueue()
        {
            Gen.Int.List[0, 5].Select(l => new ConcurrentQueue<int>(l))
            .SampleConcurrent(new SampleOptions<ConcurrentQueue<int>> { Size = 100 },
                equal: (actual, pos) => actual.OrderBy(i => i).SequenceEqual(pos.OrderBy(i => i)),
                Gen.Int.Select<int, (string, Action<ConcurrentQueue<int>>)>(i => ($"Enqueue({i})", q => q.Enqueue(i))),
                Gen.Const<(string, Action<ConcurrentQueue<int>>)>(("TryDequeue()", q => q.TryDequeue(out _)))
            );
        }
    }
}