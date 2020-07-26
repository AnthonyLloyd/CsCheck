/* The Computer Language Benchmarks Game
   https://salsa.debian.org/benchmarksgame-team/benchmarksgame/
 
   contributed by Jesper Meyer
*/

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

using Xunit;
using CsCheck;

namespace Tests
{
    public class SpectralNormTests
    {
        readonly Action<string> writeLine;
        public SpectralNormTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        //[Fact]
        public void SpectralNorm_Faster()
        {
            Check.Faster(
                () => SpectralNormNew.Program.NotMain(new[] { "5500" }),
                () => SpectralNormOld.Program.NotMain(new[] { "5500" }),
                threads: 1
            )
            .Output(writeLine);
        }
    }
}

namespace SpectralNormNew
{
    unsafe class Program
    {
        public static string NotMain(string[] args)
        {
            int n = 100;
            if (args.Length > 0) n = int.Parse(args[0]);

            fixed (double* u = new double[n])
            fixed (double* v = new double[n])
            {
                new Span<double>(u, n).Fill(1);
                for (var i = 0; i < 10; i++)
                {
                    Mult_AtAv(u, v, n);
                    Mult_AtAv(v, u, n);
                }

                var result = Math.Sqrt(Dot(u, v, n) / Dot(v, v, n));
                return result.ToString("f9");
                //Console.WriteLine("{0:f9}", result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double A(int i, int j)
        {
            return (i + j) * (i + j + 1) / 2 + i + 1;
        }

        private static double Dot(double* v, double* u, int n)
        {
            double sum = 0;
            for (var i = 0; i < n; i++)
                sum += v[i] * u[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Mult_Av(double* v, double* outv, int n)
        {
            Parallel.For(0, n, i =>
            {
                var sum = Vector256<double>.Zero;
                for (var j = 0; j < n; j += 4)
                {
                    var b = Avx.LoadVector256(v + j);
                    var a = Vector256.Create(A(i, j), A(i, j + 1), A(i, j + 2), A(i, j + 3));
                    sum = Avx.Add(sum, Avx.Divide(b, a));
                }

                var add = Sse2.Add(sum.GetUpper(), sum.GetLower());
                add = Sse3.HorizontalAdd(add, add);
                Unsafe.WriteUnaligned(outv + i, Unsafe.As<Vector128<double>, double>(ref add));
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void Mult_Atv(double* v, double* outv, int n)
        {
            Parallel.For(0, n, i =>
            {
                var sum = Vector256<double>.Zero;
                for (var j = 0; j < n; j += 4)
                {
                    var b = Avx.LoadVector256(v + j);
                    var a = Vector256.Create(A(j, i), A(j + 1, i), A(j + 2, i), A(j + 3, i));
                    sum = Avx.Add(sum, Avx.Divide(b, a));
                }

                var add = Sse2.Add(sum.GetUpper(), sum.GetLower());
                add = Sse3.HorizontalAdd(add, add);
                var value = Unsafe.As<Vector128<double>, double>(ref add);
                Unsafe.WriteUnaligned(outv + i, value);
            });
        }

        private static void Mult_AtAv(double* v, double* outv, int n)
        {
            fixed (double* tmp = new double[n])
            {
                Mult_Av(v, tmp, n);
                Mult_Atv(tmp, outv, n);
            }
        }
    }
}

namespace SpectralNormOld
{
    unsafe class Program
    {
        public static string NotMain(string[] args)
        {
            int n = 100;
            if (args.Length > 0) n = int.Parse(args[0]);

            fixed (double* u = new double[n])
            fixed (double* v = new double[n])
            {
                new Span<double>(u, n).Fill(1);
                for (var i = 0; i < 10; i++)
                {
                    mult_AtAv(u, v, n);
                    mult_AtAv(v, u, n);
                }

                var result = Math.Sqrt(dot(u, v, n) / dot(v, v, n));
                return result.ToString("f9");
                //Console.WriteLine("{0:f9}", result);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double A(int i, int j)
        {
            return (i + j) * (i + j + 1) / 2 + i + 1;
        }

        private static double dot(double* v, double* u, int n)
        {
            double sum = 0;
            for (var i = 0; i < n; i++)
                sum += v[i] * u[i];
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void mult_Av(double* v, double* outv, int n)
        {
            Parallel.For(0, n, i =>
            {
                var sum = Vector128<double>.Zero;
                for (var j = 0; j < n; j += 2)
                {
                    var b = Sse2.LoadVector128(v + j);
                    var a = Vector128.Create(A(i, j), A(i, j + 1));
                    sum = Sse2.Add(sum, Sse2.Divide(b, a));
                }

                var add = Sse3.HorizontalAdd(sum, sum);
                var value = Unsafe.As<Vector128<double>, double>(ref add);
                Unsafe.WriteUnaligned(outv + i, value);
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static void mult_Atv(double* v, double* outv, int n)
        {
            Parallel.For(0, n, i =>
            {
                var sum = Vector128<double>.Zero;
                for (var j = 0; j < n; j += 2)
                {
                    var b = Sse2.LoadVector128(v + j);
                    var a = Vector128.Create(A(j, i), A(j + 1, i));
                    sum = Sse2.Add(sum, Sse2.Divide(b, a));
                }

                var add = Sse3.HorizontalAdd(sum, sum);
                var value = Unsafe.As<Vector128<double>, double>(ref add);
                Unsafe.WriteUnaligned(outv + i, value);
            });
        }

        private static void mult_AtAv(double* v, double* outv, int n)
        {
            fixed (double* tmp = new double[n])
            {
                mult_Av(v, tmp, n);
                mult_Atv(tmp, outv, n);
            }
        }
    }
}