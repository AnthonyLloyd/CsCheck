using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

        void Assert_Commutative<T, R>(Gen<T> gen, Func<T, T, R> operation)
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

        void Assert_Associative<T>(Gen<T> gen, Func<T, T, T> operation)
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

        double[,] MulIJK(double[,] a, double[,] b)
        {
            int I = a.GetLength(0), J = a.GetLength(1), K = b.GetLength(1);
            var c = new double[I, K];
            for (int i = 0; i < I; i++)
                for (int j = 0; j < J; j++)
                    for (int k = 0; k < K; k++)
                        c[i, k] += a[i, j] * b[j, k];
            return c;
        }

        double[,] MulIKJ(double[,] a, double[,] b)
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
            int I = 10, J = 7, K = 19;
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
                i => d.AddOrUpdate(i, 0, (_,v) => 1 -v),
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

        //[Fact]
        //public void Version()
        //{
        //    Gen.Select(Gen.Byte, Gen.Byte, Gen.Byte)
        //    .Select(t => new Version(t.V0, t.V1, t.V2))
        //    .Array[0, 100]
        //    .Sample(expected =>
        //    {
        //        var actual = (Version[])expected.Clone();
        //        Array.Reverse(actual);
        //        Assert.Equal(expected, actual);
        //    });
        //}
    }
}