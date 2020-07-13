using System;
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

        void Assert_Commutative<T, R>(Gen<T> gen, Func<T, T, R> operation)
        {
            Gen.Select(gen, gen)
            .Sample(t => Assert.Equal(operation(t.V0, t.V1), operation(t.V1, t.V0)));
        }

        [Fact]
        public void Addition_Is_Commutative()
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
        public void Multiplication_Is_Commutative()
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
        public void Addition_Is_Associative()
        {
            Assert_Associative(Gen.UInt, (x, y) => x + y);
            Assert_Associative(Gen.Int, (x, y) => x + y);
            Assert_Associative(Gen.ULong, (x, y) => x + y);
            Assert_Associative(Gen.Long, (x, y) => x + y);
        }

        [Fact]
        public void Multiplication_Is_Associative()
        {
            Assert_Associative(Gen.UInt, (x, y) => x * y);
            Assert_Associative(Gen.Int, (x, y) => x * y);
            Assert_Associative(Gen.ULong, (x, y) => x * y);
            Assert_Associative(Gen.Long, (x, y) => x * y);
        }

        void MulIJK(int n, double[,] a, double[,] b, double[,] c)
        {
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    for (int k = 0; k < n; k++)
                        c[i, k] += a[i, j] * b[j, k];
        }

        void MulIKJ(int n, double[,] a, double[,] b, double[,] c)
        {
            for (int i = 0; i < n; i++)
                for (int k = 0; k < n; k++)
                {
                    double t = 0.0;
                    for (int j = 0; j < n; j++)
                        t += a[i, j] * b[j, k];
                    c[i, k] = t;
                }
        }

        [Fact]
        public void Faster_Matrix_Multiply_Fixed()
        {
            int n = 10;
            var a = new double[n, n];
            var b = new double[n, n];
            var c = new double[n, n];
            Check.Faster(
                () => MulIKJ(n, a, b, c),
                () => MulIJK(n, a, b, c))
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Matrix_Multiply_Range()
        {
            Gen.Int[10, 50]
            .Select(n => (n, a: new double[n, n], b: new double[n, n], c: new double[n, n]))
            .Faster(
                t => MulIKJ(t.n, t.a, t.b, t.c),
                t => MulIJK(t.n, t.a, t.b, t.c))
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Linq_Fixed()
        {
            var data = new byte[1000];
            new Random(42).NextBytes(data);
            Check.Faster(
                () => data.Aggregate(0.0, (t, b) => t + b),
                () => data.Select(i => (double)i).Sum())
            .Output(writeLine);
        }

        [Fact]
        public void Faster_Linq_Random()
        {
            Gen.Byte.Array[100, 1000]
            .Faster(
                data => data.Aggregate(0.0, (t, b) => t + b),
                data => data.Select(i => (double)i).Sum())
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
                data => data.Aggregate(0.0, (t, b) => t + b))
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
                })
            .Output(writeLine);
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