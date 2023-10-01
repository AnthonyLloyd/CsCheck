namespace Tests;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using CsCheck;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class StableSumTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Examples()
    {
        Assert.Equal(20000, MathStable.Sum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e104, MathStable.Sum([10000, 1e104, 10000]));
        Assert.Equal(1e104, MathStable.Sum([10000, 1e104]));
        Assert.Equal(10000, MathStable.Sum([10000, 1e-104]));
        Assert.Equal(1e-104, MathStable.Sum([10000, 1e-104, -10000]));
        Assert.Equal(-1e104, MathStable.Sum([10000, 1e-104, 10000, -1e104]));
    }

    [Fact]
    public void Shuffle_Check()
    {
        Gen.Double[-2_000_000_000, 2_000_000_000, 2_000_000_000].Array[10000, 10000]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathStable.Sum(original);
            var shuffledSum = MathStable.Sum(shuffled);
            return originalSum == shuffledSum;
        });
    }

    [Fact]
    public void Sort_Perf()
    {
        Gen.Double[-100_000_000, 100_000_000, 100_000_000].Array[2, 100]
        .Faster(
            a => MathStable.Sum(a) * 0.0,
            a => MathStableBenchmarks.Sum_Ordered(a) * 0.0
        )
        .Output(output.WriteLine);
    }

    //[Fact]
    private void BenchmarkDotNet_Perf()
    {
        var logger = new AccumulationLogger();
        BenchmarkRunner.Run<MathStableBenchmarks>(
            DefaultConfig.Instance.AddLogger(logger)
        );
        output.WriteLine(logger.GetLog());
    }
}

public static class MathStable
{
    public static double Sum(double[] values)
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

    public static double Sum_Enumerable(IEnumerable<double> values)
    {
        Span<double> partials = stackalloc double[16];
        int count = 0;
        foreach (var v in values)
        {
            var hi = v;
            int i = 0;
            for (int j = 0; j < count; j++)
            {
                var lo = partials[j];
                (hi, lo) = (
                    hi + lo,
                    Math.Abs(hi) < Math.Abs(lo) ? hi - (lo + hi - lo) : lo - (hi + lo - hi)
                );
                if (lo != 0.0)
                    partials[i++] = lo;
            }
            if (hi != 0.0)
            {
                if (i == partials.Length)
                    Resize(ref partials);
                partials[i++] = hi;
            }
            count = i;
        }
        return Sum(partials[..count]);
    }

    public static double Sum_IReadOnlyList(IReadOnlyList<double> values)
    {
        Span<double> partials = stackalloc double[16];
        int count = 0;
        foreach (var v in values)
        {
            var hi = v;
            int i = 0;
            for (int j = 0; j < count; j++)
            {
                var lo = partials[j];
                (hi, lo) = (
                    hi + lo,
                    Math.Abs(hi) < Math.Abs(lo) ? hi - (lo + hi - lo) : lo - (hi + lo - hi)
                );
                if (lo != 0.0)
                    partials[i++] = lo;
            }
            if (hi != 0.0)
            {
                if (i == partials.Length)
                    Resize(ref partials);
                partials[i++] = hi;
            }
            count = i;
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

    public static unsafe double Sum_Unsafe(double[] values)
    {
        double* partials = stackalloc double[16];
        int length = 16;
        int count = 0;
        foreach (var v in values)
        {
            var hi = v;
            int i = 0;
            for (int j = 0; j < count; j++)
            {
                var lo = partials[j];
                (hi, lo) = (
                    hi + lo,
                    Math.Abs(hi) < Math.Abs(lo) ? hi - (lo + hi - lo) : lo - (hi + lo - hi)
                );
                if (lo != 0.0)
                    partials[i++] = lo;
            }
            if (hi != 0.0)
            {
                if (i == length)
                {
                    throw new Exception();
                }
                partials[i++] = hi;
            }
            count = i;
        }
        var sum = 0.0;
        for (int i = 0; i < count; i++)
            sum += partials[i];
        return sum;
    }
}

[MemoryDiagnoser]
public class MathStableBenchmarks
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
    public double StableSum() => MathStable.Sum(doubles);

    [Benchmark]
    public double StableSum_Enumerable() => MathStable.Sum_Enumerable(doubles);

    [Benchmark]
    public double StableSum_IReadOnlyList() => MathStable.Sum_IReadOnlyList(doubles);

    [Benchmark]
    public double StableSum_Unsafe() => MathStable.Sum_Unsafe(doubles);

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