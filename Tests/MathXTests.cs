namespace Tests;

using System;
using System.Linq;
using System.Runtime.InteropServices;
using CsCheck;
using Xunit;

public class MathXTests(Xunit.Abstractions.ITestOutputHelper output)
{
    static readonly Gen<double> genDouble = Gen.Double[-1e123, 1e123];

    [Fact]
    public void TwoSum_FastTwoSum_Are_Equal_Check()
    {
        Gen.Select(genDouble, genDouble)
        .Where((a, b) => Math.Abs(a) >= Math.Abs(b))
        .Sample((a, b) => MathX.TwoSum(a, b, out var err1) == MathX.FastTwoSum(a, b, out var err2) && err1 == err2);
    }

    [Fact]
    public void TwoSum_Twice_Check()
    {
        Gen.Select(genDouble, genDouble)
        .Sample((a, b) =>
        {
            var hi = MathX.TwoSum(a, b, out var lo);
            return hi + lo == hi
                && MathX.TwoSum(hi, lo, out var lo1) == hi && lo1 == lo
                && MathX.TwoSum(lo, hi, out var lo2) == hi && lo2 == lo;
        });
    }

    [Fact]
    public void TwoSub_Check()
    {
        Gen.Select(genDouble, genDouble)
        .Sample((a, b) =>
        {
            var hi = MathX.TwoSub(a, b, out var lo);
            return a - b == hi
                && hi + lo == hi
                && MathX.TwoSum(hi, lo, out var lo1) == hi && lo1 == lo
                && MathX.TwoSum(lo, hi, out var lo2) == hi && lo2 == lo;
        });
    }

    [Fact]
    public void TwoMul_Check()
    {
        Gen.Select(genDouble, genDouble)
        .Sample((a, b) =>
        {
            var hi = MathX.TwoMul(a, b, out var lo);
            return a * b == hi
                && hi + lo == hi
                && MathX.TwoSum(hi, lo, out var lo1) == hi && lo1 == lo
                && MathX.TwoSum(lo, hi, out var lo2) == hi && lo2 == lo;
        });
    }

    [Fact]
    public void KSum_Examples()
    {
        Assert.Equal(0, MathX.KSum([]));
        Assert.Equal(0, MathX.KSum([0]));
        Assert.Equal(13, MathX.KSum([13]));
        Assert.Equal(6, MathX.KSum([13, -7]));
        Assert.Equal(1, MathX.KSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, MathX.KSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, MathX.KSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
        Assert.NotEqual(2, MathX.KSum([1, 1e100, 1, -1e100])); // reached it's accuracy tracking limit
        Assert.NotEqual(20000, MathX.KSum([10000, 1e104, 10000, -1e104]));
        Assert.NotEqual(1e-100, MathX.KSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50]));
    }

    [Fact]
    public void NSum_Examples()
    {
        Assert.Equal(0, MathX.NSum([]));
        Assert.Equal(0, MathX.NSum([0]));
        Assert.Equal(13, MathX.NSum([13]));
        Assert.Equal(6, MathX.NSum([13, -7]));
        Assert.Equal(1, MathX.NSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, MathX.NSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, MathX.NSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
        Assert.Equal(2, MathX.NSum([1, 1e100, 1, -1e100]));
        Assert.Equal(20000, MathX.NSum([10000, 1e104, 10000, -1e104]));
        Assert.NotEqual(1e-100, MathX.NSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50])); // reached it's accuracy tracking limit
    }

    [Fact]
    public void FSum_Examples()
    {
        Assert.Equal(0, MathX.FSum([]));
        Assert.Equal(0, MathX.FSum([0]));
        Assert.Equal(13, MathX.FSum([13]));
        Assert.Equal(6, MathX.FSum([13, -7]));
        Assert.Equal(1, MathX.FSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, MathX.FSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, MathX.FSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
        Assert.Equal(2, MathX.FSum([1, 1e100, 1, -1e100]));
        Assert.Equal(20000, MathX.FSum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e-100, MathX.FSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50]));
    }

    [Fact]
    public void NSum_Shuffle_Check()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.NSum(original);
            var shuffledSum = MathX.NSum(shuffled);
            return Check.AreClose(1, originalSum, shuffledSum);
        });
    }

    [Fact]
    public void FSum_Shuffle_Check()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original);
            var shuffledSum = MathX.FSum(shuffled);
            return Check.AreClose(1, originalSum, shuffledSum);
        });
    }

    [Fact]
    public void KSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.KSum(original);
            var shuffledSum = MathX.KSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void NSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.NSum(original);
            var shuffledSum = MathX.NSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void FSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original);
            var shuffledSum = MathX.FSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void FSum_Shuffle_Error_Distribution_Compress()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original, compress: true);
            var shuffledSum = MathX.FSum(shuffled, compress: true);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void FSum_Shuffle_Error_Distribution_Renormalise()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original, renormalise: true);
            var shuffledSum = MathX.FSum(shuffled, renormalise: true);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void FSum_Shuffle_Error_Distribution_Compress_Renormalise()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original, true, true);
            var shuffledSum = MathX.FSum(shuffled, true, true);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void FSum_Shuffle_Error_Distribution_Comparison()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var v1 = Check.UlpsBetween(MathX.FSum(original, false, false, false), MathX.FSum(shuffled, false, false, false));
            var v2 = Check.UlpsBetween(MathX.FSum(original, true, false, false), MathX.FSum(shuffled, true, false, false));
            var v3 = Check.UlpsBetween(MathX.FSum(original, false, true, false), MathX.FSum(shuffled, false, true, false));
            var v4 = Check.UlpsBetween(MathX.FSum(original, false, false, true), MathX.FSum(shuffled, false, false, true));
            var v5 = Check.UlpsBetween(MathX.FSum(original, true, false, true), MathX.FSum(shuffled, true, false, true));
            var v6 = Check.UlpsBetween(MathX.FSum(original, false, true, true), MathX.FSum(shuffled, false, true, true));
            return $"{v1}_{v2}_{v3}_{v4}_{v5}_{v6}";
        }, output.WriteLine/*, time: 10*/);
    }
 //   Passed Tests.MathXTests.FSum_Shuffle_Error_Distribution_Comparison [14 m 29 s]
 // Standard Output Messages:
 //|             |       Count |       % |    Median |   Lower Q |   Upper Q |   Minimum |       Maximum |
 //|-------------|------------:|--------:|----------:|----------:|----------:|----------:|--------------:|
 //| 0_0_0_0_0_0 | 399,999,616 | 100.00% | 30.0864μs | 12.9196μs | 57.2142μs |  0.2000μs | 16,827.5000μs |
 //| 1_0_0_0_0_0 |         299 |   0.00% | 29.4548μs | 16.3683μs | 46.2734μs |  2.5000μs |     91.6000μs |
 //| 1_1_1_1_1_1 |          85 |   0.00% | 40.5411μs | 22.8298μs | 60.4956μs |  2.1000μs |    102.9000μs |


  [Fact]
    public void FSum_Compress_Needed_Example()
    {
        var weights = new double[] { -8485E-81, -68, 11d / 3, -5623E-76, 47E-55, -19, 88, 134E-33 };
        var shuffled = new double[] { -8485E-81, 47E-55, -19, 11d / 3, 134E-33, -5623E-76, 88, -68 };
        var weightsFSum = weights.FSum();
        var shuffledFSum = shuffled.FSum();
        Assert.NotEqual(weightsFSum, shuffledFSum);
        var weightsFSumCompress = weights.FSum(compress: true);
        var shuffledFSumCompress = shuffled.FSum(compress: true);
        Assert.Equal(weightsFSumCompress, shuffledFSumCompress);
    }

    [Fact]
    public void NSum_FSum_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .Sample(values =>
        {
            var fsumSum = MathX.FSum(values);
            var nsumSum = MathX.NSum(values);
            return Check.UlpsBetween(fsumSum, nsumSum).ToString().PadLeft(5);
        }, output.WriteLine/*, time: 10*/);
    }

    [Fact]
    public void SSum_Examples()
    {
        Assert.Equal(0, MathX.SSum([]));
        Assert.Equal(0, MathX.SSum([0]));
        Assert.Equal(13, MathX.SSum([13]));
        Assert.Equal(13, MathX.SSum([7, 13, -7]));
        Assert.Equal(1, MathX.SSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, MathX.SSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, MathX.SSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
        Assert.Equal(2, MathX.SSum([1, 1e100, 1, -1e100]));
        Assert.Equal(20000, MathX.SSum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e-100, MathX.SSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50]));
    }

    [Fact]
    public void SSum_Shuffle_Check()
    {
        genDouble.Array[2, 10]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.SSum(original);
            var shuffledSum = MathX.SSum(shuffled);
            return shuffledSum == originalSum;
        });
    }

    [Fact]
    public void SSum_Negative_Check()
    {
        genDouble.Array[2, 10]
        .Sample(original =>
        {
            var originalSum = MathX.SSum(original);
            for (int i = 0; i < original.Length; i++)
                original[i] *= -1;
            var negativeSum = MathX.SSum(original);
            return negativeSum == -originalSum;
        });
    }

    [Fact]
    public void SSum_Shuffle_Negative_Check()
    {
        genDouble.Array[2, 10]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.SSum(original);
            for (int i = 0; i < shuffled.Length; i++)
                shuffled[i] *= -1;
            var shuffledSum = MathX.SSum(shuffled);
            return shuffledSum == -originalSum;
        });
    }

    [Fact]
    public void FSum_Vs_SSum_Perf()
    {
        genDouble.Array[2, 100]
        .Faster(
            values => values.FSum(),
            values => values.SSum(),
            Check.EqualSkip,
            writeLine: output.WriteLine
        );
    }

    [Fact]
    public void Mantissa()
    {
        genDouble.Sample(d =>
        {
            var m1 = MathX.Mantissa(d, out var e1);
            var d1 = Math.ScaleB(m1, e1);
            return d == d1;
        });
    }
}