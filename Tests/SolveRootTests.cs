namespace Tests;

using System;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using CsCheck;

public class SolveRootTests(Xunit.Abstractions.ITestOutputHelper output)
{
    readonly Action<string> writeLine = output.WriteLine;

    public static bool BoundsZero(double a, double b) => (a < 0.0 && b > 0.0) || (b < 0.0 && a > 0.0);

    public static double LinearRoot(double a, double fa, double b, double fb)
    {
        var x = fa / (fa - fb);
        return a - x * a + x * b;
    }

    public static double QuadraticRoot(double a, double fa, double b, double fb, double c, double fc)
    {
        var r = (fb - fa) / (b - a) - (fc - fb) / (c - b);
        var w = (fc - fa) / (c - a) + r;
        r = w * w - fa * 4 * r / (a - c);
        r = r <= 0 ? 0 : Math.Sqrt(r);
        var x = a - fa * 2 / (w + r);
        if (a < x && x < b) return x;
        x = a - fa * 2 / (w - r);
        if (a < x && x < b) return x;
        return LinearRoot(a, fa, b, fb);
    }

    public static double InverseQuadraticRoot(double a, double fa, double b, double fb, double c, double fc)
    {
        var x = a * fb * fc / ((fa - fb) * (fa - fc)) + b * fa * fc / ((fb - fa) * (fb - fc)) + c * fa * fb / ((fc - fa) * (fc - fb));
        if (a < x && x < b) return x;
        return QuadraticRoot(a, fa, b, fb, c, fc);
    }

    /// <summary>
    /// Finds x the root f(x) = 0 accurate to tol where a and b (a<b) bound a root i.e. f(a)f(b) < 0.
    /// </summary>
    /// <param name="tol">The tolerance of the root required.</param>
    /// <param name="f">The function to find the root of.</param>
    /// <param name="a">The lower boundary.</param>
    /// <param name="b">The upper boundary.</param>
    /// <returns>The root x accurate to tol.</returns>
    public static double Root(double tol, Func<double, double> f, double a, double b)
        => Root(tol, f, a, b, a + (b - a) * 0.2, b - (b - a) * 0.2);

    /// <summary>
    /// Finds x the root f(x) = 0 accurate to tol where a and b (a<ai<bi<b) bound a root i.e. f(a)f(b) < 0.
    /// </summary>
    /// <param name="tol">The tolerance of the root required.</param>
    /// <param name="f">The function to find the root of.</param>
    /// <param name="a">The lower boundary.</param>
    /// <param name="b">The upper boundary.</param>
    /// <param name="ai">The lower inner region.</param>
    /// <param name="bi">The upper inner region.</param>
    /// <returns>The root x accurate to tol.</returns>
    public static double Root(double tol, Func<double, double> f, double a, double b, double ai, double bi)
    {
        static double RootInner(double tol, Func<double, double> f, double a, double fa, double b, double fb, double c, double fc)
        {
            int level = 1;
            while (b - a > tol * 2)
            {
                double x;
                if (b - a < tol * 4 || level >= 2)
                {
                    x = (a + b) * 0.5;
                }
                else
                {
                    x = level == 0 ? QuadraticRoot(a, fa, b, fb, c, fc) : InverseQuadraticRoot(a, fa, b, fb, c, fc);
                    x = Math.Min(b - tol * 1.99, Math.Max(a + tol * 1.99, x));
                }
                var fx = f(x); if (fx == 0.0) return x;
                if (BoundsZero(fa, fx))
                {
                    level = b - x < 0.4 * (b - a) ? level + 1 : 0;
                    if (c > b || b - x < a - c) { c = b; fc = fb; }
                    b = x; fb = fx;
                }
                else
                {
                    level = x - a < 0.4 * (b - a) ? level + 1 : 0;
                    if (c < a || x - a < c - b) { c = a; fc = fa; }
                    a = x; fa = fx;
                }
            }
            return (a + b) * 0.5;
        }
        var fai = f(ai); if (fai == 0.0) return ai;
        var fbi = f(bi); if (fbi == 0.0) return bi;
        if (BoundsZero(fai, fbi)) return RootInner(tol, f, ai, fai, bi, fbi, double.PositiveInfinity, 0);
        var lx = LinearRoot(ai, fai, bi, fbi);
        if (Math.Abs(fai) < Math.Abs(fbi))
        {
            if (lx > a && lx < ai)
            {
                var ai2 = lx - (lx - a) * 0.2;
                var fai2 = f(ai2); if (fai2 == 0.0) return ai2;
                if (BoundsZero(fai2, fai)) return RootInner(tol, f, ai2, fai2, ai, fai, bi, fbi);
                var fa = f(a); if (fa == 0.0) return a;
                if (BoundsZero(fa, fai2)) return RootInner(tol, f, a, fa, ai2, fai2, ai, fai);
                var fb = f(b); if (fb == 0.0) return b;
                return RootInner(tol, f, bi, fbi, b, fb, ai, fai);
            }
            else
            {
                var fa = f(a); if (fa == 0.0) return a;
                if (BoundsZero(fa, fai)) return RootInner(tol, f, a, fa, ai, fai, bi, fbi);
                var fb = f(b); if (fb == 0.0) return b;
                return RootInner(tol, f, bi, fbi, b, fb, ai, fai);
            }
        }
        else
        {
            if (lx > bi && lx < b)
            {
                var bi2 = lx + (b - lx) * 0.2;
                var fbi2 = f(bi2); if (fbi2 == 0.0) return bi2;
                if (BoundsZero(fbi, fbi2)) return RootInner(tol, f, bi, fbi, bi2, fbi2, ai, fai);
                var fb = f(b); if (fb == 0.0) return b;
                if (BoundsZero(fbi2, fb)) return RootInner(tol, f, bi2, fbi2, b, fb, bi, fbi);
                var fa = f(a); if (fa == 0.0) return a;
                return RootInner(tol, f, a, fa, ai, fai, bi, fbi);
            }
            else
            {
                var fb = f(b); if (fb == 0.0) return b;
                if (BoundsZero(fbi, fb)) return RootInner(tol, f, bi, fbi, b, fb, ai, fai);
                var fa = f(a); if (fa == 0.0) return a;
                return RootInner(tol, f, a, fa, ai, fai, bi, fbi);
            }
        }
    }

    [Fact]
    public void LinearRoot_Bound()
    {
        var genD = Gen.Double[-10_000, 10_000];
        Gen.Select(genD, genD, genD, genD)
        .Where((a, fa, b, fb) => a < b && BoundsZero(fa, fb))
        .Select((a, fa, b, fb) => (a, fa, b, fb, LinearRoot(a, fa, b, fb)))
        .Sample((a, _, b, _, x) => a <= x && x <= b);
    }

    [Fact]
    public void QuadraticRoot_Increasing()
    {
        static double f(double x) => x * x - 100.0;
        Assert.Equal(10.0, QuadraticRoot(9.4, f(9.4), 11.7, f(11.7), 20.11, f(20.11)));
    }

    [Fact]
    public void QuadraticRoot_Decreasing()
    {
        static double f(double x) => x * x - 100.0;
        Assert.Equal(-10.0, QuadraticRoot(-11.0, f(-11.0), -8.7, f(-8.7), -20.0, f(-20.0)));
    }

    [Fact]
    public void QuadraticRoot_Bound()
    {
        var genD = Gen.Double[-10_000, 10_000];
        Gen.Select(genD, genD, genD, genD, genD, genD)
        .Where((a, fa, b, fb, c, _) => a < b && (c < a || c > b) && BoundsZero(fa, fb))
        .Select((a, fa, b, fb, c, fc) => (a, fa, b, fb, c, fc, QuadraticRoot(a, fa, b, fb, c, fc)))
        .Sample((a, _, b, _, _, _, x) => a <= x && x <= b);
    }

    [Fact]
    public void QuadraticRoot_Correct()
    {
        static bool AreClose(double a, double b) => Check.AreClose(1e-7, 1e-7, a, b);
        Gen.Select(Gen.Double[-10, -1], Gen.Double[1, 10])
        .SelectMany((root1, root2) =>
        {
            double f(double x) => (x - root1) * (x - root2);
            var genD = Gen.Double[root1 * 3, root2 * 3];
            return Gen.Select(genD, genD, genD)
                   .Where((a, b, c) => a < b && ((c < a && !AreClose(c, a)) || (c > b && !AreClose(c, b))))
                   .Select((a, b, c) => (a, f(a), b, f(b), c, f(c)))
                   .Where((a, fa, b, fb, c, fc) => BoundsZero(fa, fb))
                   .Select((a, fa, b, fb, c, fc) => (root1, root2, a, b, c, QuadraticRoot(a, fa, b, fb, c, fc)));
        })
        .Sample((root1, root2, a, b, c, x) => AreClose(root1, x) || AreClose(root2, x));
    }

    static (int, int[]) TestSolver(double tol, Func<double, Func<double, double>, double, double, double> solver)
    {
        var problems = TestProblems().ToArray();
        Assert.Equal(154, problems.Length);
        var counts = new int[problems.Length];
        var count = 0;
        for (int i = 0; i < problems.Length; i++)
        {
            var (F, Min, Max) = problems[i];
            counts[i] = -count;
            var x = solver(tol, x => { count++; return F(x); }, Min, Max);
            counts[i] += count;
            Assert.True(BoundsZero(F(x - tol), F(x + tol)) || F(x) == 0.0);
        }
        return (count, counts);
    }

    [Fact]
    public void Root_TestProblems_4() => Assert.Equal(1937, TestSolver(1e-4, Root).Item1);

    [Fact]
    public void Root_TestProblems_5() => Assert.Equal(2063, TestSolver(1e-5, Root).Item1);

    [Fact]
    public void Brent_TestProblems_6() => Assert.Equal(2763, TestSolver(1e-6, Brent).Item1);

    [Fact]
    public void Root_TestProblems_6() => Assert.Equal(2144, TestSolver(1e-6, Root).Item1);

    [Fact]
    public void Brent_TestProblems_7() => Assert.Equal(2816, TestSolver(1e-7, Brent).Item1);

    [Fact]
    public void Root_TestProblems_7() => Assert.Equal(2237, TestSolver(1e-7, Root).Item1);

    [Fact]
    public void Brent_TestProblems_9() => Assert.Equal(2889, TestSolver(1e-9, Brent).Item1);

    [Fact]
    public void Root_TestProblems_9() => Assert.InRange(TestSolver(1e-9, Root).Item1, 2292, 2293);

    [Fact]
    public void Brent_TestProblems_11() => Assert.Equal(2935, TestSolver(1e-11, Brent).Item1);

    [Fact]
    public void Root_TestProblems_11() => Assert.InRange(TestSolver(1e-11, Root).Item1, 2328, 2330);

    [Fact]
    public void Root_TestProblems_Compare()
    {
        var rootCounts = TestSolver(1e-11, Root).Item2;
        var brentCounts = TestSolver(1e-11, Brent).Item2;
        var check =
            rootCounts.Zip(brentCounts)
            .Select((t, i) => (t.First, t.Second, i))
            .Where(t => t.First > t.Second)
            .OrderBy(t => t.Second - t.First)
            .ThenBy(t => ((double)(t.Second - t.First)) / t.Second)
            .Select(t => t.i + ": " + t.First + " - " + t.Second);
        foreach (var s in check) writeLine(s);
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
            const string str = "Invalid starting bracket. Function must be above target on one end and below target on other end.";
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
        {
            return b;
        }
    }

    [Fact]
    public void BondSpreadProblem()
    {
        const double tol = 1e-6, coupon = 0.075, interestRate = 0.035;
        const int years = 20;
        static double BondPrice(double spread)
        {
            double pv = 0.0;
            for (int t = 1; t < years * 2; t++)
                pv += coupon * 0.5 * Math.Pow(1 + interestRate + spread, -0.5 * t);
            pv += (1 + coupon * 0.5) * Math.Pow(1 + interestRate + spread, -years);
            return pv;
        }
        var countBrent = 0;
        var spreadBrent = Brent(tol, x => { countBrent++; return 0.9 - BondPrice(x); }, -0.1, 1);
        var countRoot = 0;
        var spreadRoot = Root(tol, x => { countRoot++; return 0.9 - BondPrice(x); }, -0.1, 1);
        var countRoot2 = 0;
        var spreadRoot2 = Root(tol, x => { countRoot2++; return 0.9 - BondPrice(x); }, -0.1, 1, 0.0423, 0.0623);
        Assert.True(Math.Abs(spreadRoot - spreadBrent) < tol * 2);
        Assert.True(Math.Abs(spreadRoot2 - spreadBrent) < tol * 2);
        Assert.Equal(12, countBrent);
        Assert.Equal(10, countRoot);
        Assert.Equal(6, countRoot2);
    }

    [Fact]
    public void OptionVolatilityProblem()
    {
        const double tol = 1e-6;
        static double CND(double x)
        {
            var l = Math.Abs(x);
            var k = 1.0 / (1.0 + 0.2316419 * l);
            var cnd = 1.0 - 1.0 / Math.Sqrt(2 * Convert.ToDouble(Math.PI.ToString())) * Math.Exp(-l * l / 2.0) *
                (0.31938153 * k + -0.356563782 * k * k + 1.781477937 * Math.Pow(k, 3.0) + -1.821255978 * Math.Pow(k, 4.0)
                + 1.330274429 * Math.Pow(k, 5.0));
            return x < 0 ? 1.0 - cnd : cnd;
        }
        static double BlackScholes(bool call, double s, double x, double t, double r, double v)
        {
            var d1 = (Math.Log(s / x) + (r + v * v / 2.0) * t) / (v * Math.Sqrt(t));
            var d2 = d1 - v * Math.Sqrt(t);
            return call ? s * CND(d1) - x * Math.Exp(-r * t) * CND(d2) : x * Math.Exp(-r * t) * CND(-d2) - s * CND(-d1);
        }
        var countBrent = 0;
        var volatilityBrent = Brent(tol, x => { countBrent++; return BlackScholes(true, 9, 10, 2, 0.02, x) - 1; }, 0, 1);
        var countRoot = 0;
        var volatilityRoot = Root(tol, x => { countRoot++; return BlackScholes(true, 9, 10, 2, 0.02, x) - 1; }, 0, 1);
        var countRoot2 = 0;
        var volatilityRoot2 = Root(tol, x => { countRoot2++; return BlackScholes(true, 9, 10, 2, 0.02, x) - 1; }, 0, 1, 0.145, 0.345);
        Assert.True(Math.Abs(volatilityRoot - volatilityBrent) < tol * 2);
        Assert.True(Math.Abs(volatilityRoot2 - volatilityBrent) < tol * 2);
        Assert.Equal(7, countBrent);
        Assert.Equal(6, countRoot);
        Assert.Equal(5, countRoot2);
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
        {
            yield return (
                x => -2.0 * Enumerable.Range(1, 20).Sum(i => Sqr(2 * i - 5.0) / Cube(x - i * i)),
                Sqr(n) + 1e-9,
                Sqr(n + 1) - 1e-9
            );
        }

        foreach (var (a, b) in new[] { (-40, -1), (-100, -2), (-200, -3) })
        {
            yield return (
                x => a * x * Math.Exp(b * x),
                -9,
                31
            );
        }

        foreach (var a in new[] { 0.2, 1 })
        {
            foreach (var n in Range(4, 2, 12))
            {
                yield return (
                    x => Math.Pow(x, n) - a,
                    0,
                    5
                );
            }
        }

        foreach (var n in Range(8, 2, 14))
        {
            yield return (
                x => Math.Pow(x, n) - 1,
                -0.95,
                4.05
            );
        }

        yield return (
            x => Math.Sin(x) - 0.5,
            0,
            1.5
        );
        foreach (var n in Range(1, 1, 5).Concat(Range(20, 20, 100)))
        {
            yield return (
                x => 2 * x * Math.Exp(-n) - 2 * Math.Exp(-n * x) + 1,
                0,
                1
            );
        }

        foreach (var n in new[] { 5, 10, 20 })
        {
            yield return (
                x => (1 + Sqr(1 - n)) * x - Sqr(1 - n * x),
                0,
                1
            );
        }

        foreach (var n in new[] { 2, 5, 10, 15, 20 })
        {
            yield return (
                x => Sqr(x) - Math.Pow(1 - x, n),
                0,
                1
            );
        }

        foreach (var n in new[] { 1, 2, 4, 5, 8, 15, 20 })
        {
            yield return (
                x => (1 + Math.Pow(1 - n, 4)) * x - Math.Pow(1 - n * x, 4),
                0,
                1
            );
        }

        foreach (var n in new[] { 1, 5, 10, 15, 20 })
        {
            yield return (
                x => Math.Exp(-n * x) * (x - 1) + Math.Pow(x, n),
                0,
                1
            );
        }

        foreach (var n in new[] { 2, 5, 15, 20 })
        {
            yield return (
                x => (n * x - 1) / ((n - 1) * x),
                0.01,
                1
            );
        }

        foreach (var n in Range(2, 1, 6).Concat(Range(7, 2, 33)))
        {
            yield return (
                x => Math.Pow(x, 1.0 / n) - Math.Pow(n, 1.0 / n),
                0,
                100
            );
        }

        yield return (
            x => x == 0 ? 0 : x * Math.Exp(-Math.Pow(x, -2)),
            -1,
            4
        );
        foreach (var n in Range(1, 1, 40))
        {
            yield return (
                x => x >= 0 ? 0.05 * n * (x / 1.5 + Math.Sin(x) - 1) : -0.05 * n,
                -1e4,
                Math.PI * 0.5
            );
        }

        foreach (var n in Range(20, 1, 40).Concat(Range(100, 100, 1000)))
        {
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