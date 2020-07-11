using System;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class CheckTests
    {
        readonly Action<string> writeLine;
        public CheckTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

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
                data => data.Select(i => (double)i).Sum(),
                Assert.Equal)
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