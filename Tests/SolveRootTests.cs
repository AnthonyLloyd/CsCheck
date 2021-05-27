using System;
using System.Linq;
using Xunit;
using CsCheck;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace Tests
{
    public class SolveRootTests
    {
        readonly Action<string> writeLine;
        public SolveRootTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        public static bool BoundsZero(double a, double b) => (a < 0.0 && b > 0.0) || (b < 0.0 && a > 0.0);

        public static double LinearRoot(double a, double fa, double b, double fb) => (a * fb - b * fa) / (fb - fa);

        [Fact]
        public void LinearRoot_Bound()
        {
            var genD = Gen.Double[-10_000, 10_000];
            Gen.Select(genD, genD, genD, genD)
            .Where((a, fa, b, fb) => a < b && BoundsZero(fa, fb))
            .Select((a, fa, b, fb) => (a, fa, b, fb, LinearRoot(a, fa, b, fb)))
            .Sample((a, fa, b, fb, x) => a <= x && x <= b);
        }

        public static double QuadraticRoot(double a, double fa, double b, double fb, double c, double fc)
        {
            var r = (fb - fa) / (b - a) - (fc - fb) / (c - b);
            var w = (fc - fa) / (c - a) + r;
            r = w * w - 4.0 * fa * r / (a - c);
            r = r <= 0.0 ? 0.0 : Math.Sqrt(r);
            var x = a - 2.0 * fa / (w + r);
            if (a < x && x < b) return x;
            x = a - 2.0 * fa / (w - r);
            if (a < x && x < b) return x;
            return LinearRoot(a, fa, b, fb);
        }

        public static double InverseQuadraticRoot(double a, double fa, double b, double fb, double c, double fc)
        {
            if (fb == fc || fa == fc || c == double.PositiveInfinity) return LinearRoot(a, fa, b, fb);
            var x = a * fb * fc / ((fa - fb) * (fa - fc)) + b * fa * fc / ((fb - fa) * (fb - fc)) + c * fa * fb / ((fc - fa) * (fc - fb));
            if (a < x && x < b) return x;
            return LinearRoot(a, fa, b, fb);
        }

        [Fact]
        public void QuadraticRoot_Increasing()
        {
            static double f(double x) => x * x - 100.0;
            var x = QuadraticRoot(9.4, f(9.4), 11.7, f(11.7), 20.11, f(20.11));
            Assert.Equal(10.0, x);
        }

        [Fact]
        public void QuadraticRoot_Decreasing()
        {
            static double f(double x) => x * x - 100.0;
            var x = QuadraticRoot(-11.0, f(-11.0), -8.7, f(-8.7), -20.0, f(-20.0));
            Assert.Equal(-10.0, x);
        }

        [Fact]
        public void QuadraticRoot_Bound()
        {
            var genD = Gen.Double[-10_000, 10_000];
            Gen.Select(genD, genD, genD, genD, genD, genD)
            .Where((a, fa, b, fb, c, fc) => a < b && (c < a || c > b) && BoundsZero(fa, fb))
            .Select((a, fa, b, fb, c, fc) => (a, fa, b, fb, c, fc, QuadraticRoot(a, fa, b, fb, c, fc)))
            .Sample((a, fa, b, fb, c, fc, x) => a <= x && x <= b);
        }

        static int TestSolver(double tol, Func<double, Func<double, double>, double, double, double> solver)
        {
            var count = 0;
            foreach (var (F, Min, Max) in TestProblems().ToArray())
            {
                var x = solver(tol, x => { count++; return F(x); }, Min, Max);
                Assert.True(BoundsZero(F(x - tol), F(x + tol)) || F(x) == 0.0);
            }
            return count;
        }

        [Fact]
        public void Brent_TestProblems()
        {
            Assert.Equal(2816, TestSolver(1e-7, Brent));
        }

        [Fact]
        public void Root_TestProblems()
        {
            Assert.Equal(2544, TestSolver(1e-7, Root));
        }

        public static double Root(double tol, Func<double, double> f, double a, double b)
        {
            const double F = 0.4;
            var fa = f(a);
            if (fa == 0.0) return a;
            var fb = f(b);
            if (fb == 0.0) return b;
            if (!BoundsZero(fa, fb)) throw new Exception($"f(a)={fa} and f(b)={fb} do not bound zero");
            double c = double.PositiveInfinity, fc = 0;
            int shit = 1;
            while (b - a > tol * 2)
            {
                double x = shit == 0 ? QuadraticRoot(a, fa, b, fb, c, fc)
                         : shit == 1 ? InverseQuadraticRoot(a, fa, b, fb, c, fc)
                         : (a + b) * 0.5;
                x = Math.Min(b - tol * 1.99999, Math.Max(a + tol * 1.99999, x));
                var fx = f(x);
                if (fx == 0.0) return x;
                if (BoundsZero(fa, fx))
                {
                    shit = b - x < F * (b - a) ? shit + 1 : 0;
                    if (c > b || b - x < a - c) { c = b; fc = fb; }
                    b = x; fb = fx;
                }
                else
                {
                    shit = x - a < F * (b - a) ? shit + 1 : 0;
                    if (c < a || x - a < c - b) { c = a; fc = fa; }
                    a = x; fa = fx;
                }
            }
            return (a + b) * 0.5;
        }


        public static double Brent(double tol, Func<double, double> f, double a, double b)
        {
            // Implementation and notation based on Chapter 4 in
            // "Algorithms for Minimization without Derivatives"
            // by Richard Brent.

            var fa = f(a);
            var fb = f(b);

            if (fa * fb > 0.0)
            {
                string str = "Invalid starting bracket. Function must be above target on one end and below target on other end.";
                string msg = string.Format("{0} Target: {1}. f(left) = {2}. f(right) = {3}", str, 0, fa + 0, fb + 0);
                throw new ArgumentException(msg);
            }

            label_int:
            double c = a, fc = fa, d = b - a, e = d;
            label_ext:
            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b; b = c; c = a;
                fa = fb; fb = fc; fc = fa;
            }

            var m = 0.5 * (c - b);
            if (Math.Abs(m) > tol && fb != 0.0) // exact comparison with 0 is OK here
            {
                // See if bisection is forced
                if (Math.Abs(e) < tol || Math.Abs(fa) <= Math.Abs(fb))
                {
                    d = e = m;
                }
                else
                {
                    var s = fb / fa;
                    double p, q;
                    if (a == c)
                    {
                        // linear interpolation
                        p = 2.0 * m * s; q = 1.0 - s;
                    }
                    else
                    {
                        // Inverse quadratic interpolation
                        q = fa / fc; var r = fb / fc;
                        p = s * (2.0 * m * q * (q - r) - (b - a) * (r - 1.0));
                        q = (q - 1.0) * (r - 1.0) * (s - 1.0);
                    }
                    if (p > 0.0)
                        q = -q;
                    else
                        p = -p;
                    s = e; e = d;
                    if (2.0 * p < 3.0 * m * q - Math.Abs(tol * q) && p < Math.Abs(0.5 * s * q))
                        d = p / q;
                    else
                        d = e = m;
                }
                a = b; fa = fb;
                if (Math.Abs(d) > tol)
                    b += d;
                else if (m > 0.0)
                    b += tol;
                else
                    b -= tol;

                fb = f(b);
                if (fb > 0.0 == fc > 0.0)
                    goto label_int;
                else
                    goto label_ext;
            }
            else
                return b;
        }

        static IEnumerable<(Func<double, double> F, double Min, double Max)> TestProblems()
        {
            static double Sqr(double x) => x * x;
            static double Cube(double x) => x * x * x;
            static IEnumerable<int> Range(int start, int step, int finish)
            {
                for (int i = start; i <= finish; i += step)
                    yield return i;
            }

            yield return (
                x => Math.Sin(x) - 0.50 * x,
                Math.PI * 0.5,
                Math.PI
            );
            for (int n = 1; n <= 10; n++)
                yield return (
                    x => -2.0 * Enumerable.Range(1, 20).Sum(i => Sqr(2 * i - 5.0) / Cube(x - i * i)),
                    Sqr(n) + 1e-9,
                    Sqr(n + 1) - 1e-9
                );
            foreach (var (a, b) in new[] { (-40, -1), (-100, -2), (-200, -3) })
                yield return (
                    x => a * x * Math.Exp(b * x),
                    -9,
                    31
                );
            foreach (var a in new[] { 0.2, 1 })
                foreach (var n in Range(4, 2, 12))
                    yield return (
                        x => Math.Pow(x, n) - a,
                        0,
                        5
                    );
            foreach (var n in Range(8, 2, 14))
                yield return (
                    x => Math.Pow(x, n) - 1,
                    -0.95,
                    4.05
                );
            yield return (
                x => Math.Sin(x) - 0.5,
                0,
                1.5
            );
            foreach (var n in Range(1, 1, 5).Concat(Range(20, 20, 100)))
                yield return (
                    x => 2 * x * Math.Exp(-n) - 2 * Math.Exp(-n * x) + 1,
                    0,
                    1
                );
            foreach (var n in new[] { 5, 10, 20 })
                yield return (
                    x => (1 + Sqr(1 - n)) * x - Sqr(1 - n * x),
                    0,
                    1
                );
            foreach (var n in new[] { 2, 5, 10, 15, 20 })
                yield return (
                    x => Sqr(x) - Math.Pow(1 - x, n),
                    0,
                    1
                );
            foreach (var n in new[] { 1, 2, 4, 5, 8, 15, 20 })
                yield return (
                    x => (1 + Math.Pow(1 - n, 4)) * x - Math.Pow(1 - n * x, 4),
                    0,
                    1
                );
            foreach (var n in new[] { 1, 5, 10, 15, 20 })
                yield return (
                    x => Math.Exp(-n * x) * (x - 1) + Math.Pow(x, n),
                    0,
                    1
                );
            foreach (var n in new[] { 2, 5, 15, 20 })
                yield return (
                    x => (n * x - 1) / ((n - 1) * x),
                    0.01,
                    1
                );
            foreach (var n in Range(2, 1, 6).Concat(Range(7, 2, 33)))
                yield return (
                    x => Math.Pow(x, 1.0 / n) - Math.Pow(n, 1.0 / n),
                    0,
                    100
                );
            yield return (
                x => x == 0 ? 0 : x * Math.Exp(-Math.Pow(x, -2)),
                -1,
                4
            );
            foreach (var n in Range(1, 1, 40))
                yield return (
                    x => x >= 0 ? 0.05 * n * (x / 1.5 + Math.Sin(x) - 1) : -0.05 * n,
                    -1e4,
                    Math.PI * 0.5
                );
            foreach (var n in Range(20, 1, 40).Concat(Range(100, 100, 1000)))
                yield return (
                    x => x < 0 ? -0.859
                       : x > 2e-3 / (1 + n) ? Math.E - 1.859
                       : Math.Exp((n + 1) * x / 2e-3) - 1.859,
                    -1e4,
                    1e-4
                );
        }
    }
}