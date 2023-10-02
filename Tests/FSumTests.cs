namespace Tests;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using CsCheck;
using System;
using System.Linq;
using Xunit;

public class FSumTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Examples()
    {
        Assert.Equal(0, Maths.FSum([]));
        Assert.Equal(0, Maths.FSum([0]));
        Assert.Equal(13, Maths.FSum([13]));
        Assert.Equal(6, Maths.FSum([13, -7]));
        Assert.Equal(20000, Maths.FSum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e-100, Maths.FSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50]));
        Assert.Equal(1, Maths.FSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, Maths.FSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, Maths.FSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
    }

    [Fact]
    public void Shuffle_Check()
    {
        Gen.Double[-2_000_000_000, 2_000_000_000, 2_000_000_000].Array[1000, 1000]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = Maths.FSum(original);
            var shuffledSum = Maths.FSum(shuffled);
            return originalSum == shuffledSum;
        });
    }

    [Fact]
    public void TwoSum_Perf()
    {
        Gen.Double[-2_000_000_000, 2_000_000_000, 2_000_000_000].Array[2, 100]
        .Faster(
            Maths.FSum,
            Maths.FSum_Original
        , sigma: 10, repeat: 100)
        .Output(output.WriteLine);
    }

    //[Fact]
    private void BenchmarkDotNet_Perf()
    {
        var logger = new AccumulationLogger();
        BenchmarkRunner.Run<FSumBenchmarks>(
            DefaultConfig.Instance.AddLogger(logger)
        );
        output.WriteLine(logger.GetLog());
    }
}

public static class Maths
{
    private static (double hi, double lo) TwoSum(double a, double b)
    {
        var hi = a + b;
        var a2 = hi - b;
        a -= a2;
        return (hi, a2 - hi + b + a);
    }

    public static double FSum(this double[] values)
    {
        Span<double> partials = stackalloc double[16];
        int count = 0;
        var hi = 0.0;
        var lo = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            (var v, lo) = TwoSum(values[i], lo);
            int c = 0;
            for (int j = 0; j < count; j++)
            {
                (v, var partial) = TwoSum(v, partials[j]);
                if (partial != 0.0)
                    partials[c++] = partial;
            }
            (hi, v) = TwoSum(hi, v);
            if (v != 0.0)
            {
                if (c == partials.Length)
                    Resize(ref partials);
                partials[c++] = v;
            }
            count = c;
        }
        while (--count >= 0)
            lo += partials[count];
        return lo + hi;
    }

    public static double FSum_Original(this double[] values)
    {
        Span<double> partials = stackalloc double[16];
        int count = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var hi = values[i];
            int c = 0;
            for (int j = 0; j < count; j++)
            {
                var lo = partials[j];
                (hi, lo) = (
                    hi + lo,
                    Math.Abs(hi) < Math.Abs(lo) ? hi - (lo + hi - lo) : lo - (hi + lo - hi)
                );
                if (lo != 0.0)
                    partials[c++] = lo;
            }
            if (hi != 0.0)
            {
                if (c == partials.Length)
                    Resize(ref partials);
                partials[c++] = hi;
            }
            count = c;
        }
        return Sum(partials[..count]);
    }

    private static void Resize(ref Span<double> partials)
    {
        var newPartials = new double[partials.Length * 2];
        partials.CopyTo(newPartials);
        partials = newPartials;
    }

    private static double Sum(Span<double> doubles)
    {
        var sum = 0.0;
        for (int i = 0; i < doubles.Length; i++)
            sum += doubles[i];
        return sum;
    }
}

[MemoryDiagnoser]
public class FSumBenchmarks
{
    [Params(5, 10, 100, 1000)]
    public int N;

    double[] doubles;
    [GlobalSetup]
    public void Setup()
    {
        doubles = Gen.Double[-2_000_000_000, 2_000_000_000, 2_000_000_000]
            .Array[N].Example(_ => true, seed: "0000UlDtVIY4");
    }

    [Benchmark]
    public double FSum() => Maths.FSum(doubles);


    [Benchmark(Baseline = true)]
    public double SimpleSum() => doubles.Sum();

    [Benchmark]
    public double Ordered() => Sum_Ordered(doubles);

    public static double Sum_Ordered(double[] values)
    {
        values = (double[])values.Clone();
        Array.Sort(values, (x, y) => Math.Abs(x).CompareTo(Math.Abs(y)));
        return values.Sum();
    }
}