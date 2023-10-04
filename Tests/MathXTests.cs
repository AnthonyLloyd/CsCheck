namespace Tests;

using CsCheck;
using System;
using Xunit;

public class MathXTests
{
    private static Gen<double> GenNumber(double start, double finish, int denominator)
    {
        var genInteger = start <= int.MinValue && finish >= int.MaxValue ? Gen.Int
            : Gen.Int[(int)Math.Max(Math.Ceiling(start), int.MinValue), (int)Math.Min(Math.Floor(finish), int.MaxValue)];
        var genRational = genInteger
            .SelectMany(i => Gen.Int[2, denominator].SelectMany(den => Gen.Int[1 - den, den - 1].Select(num => (double)num / den + i)))
            .Where(r => r >= start && r <= finish);
        var startExp = (int)Math.Floor(Math.Log10(Math.Abs(start)));
        var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(finish)));
        finishExp = Math.Max(finishExp, startExp);
        startExp = -finishExp;
        var genExponential = Gen.Select(Gen.Double.OneTwo, Gen.Int[startExp, finishExp], Gen.Bool)
            .Select((m, e, s) => (m - 1) * Math.Pow(10, e + 1) * (s ? -1 : 1))
            .Where(e => e >= start && e <= finish);
        return Gen.OneOf(genInteger.Cast<double>(), genRational, genExponential);
    }

    [Fact]
    public void Mantissa()
    {
        Gen.Double[-1e20, 1e20]
        .Sample(d =>
        {
            var m1 = MathX.Mantissa(d, out var e1);
            var d1 = Math.ScaleB(m1, e1);
            return d == d1;
        });
    }

    [Fact]
    public void FSum_Examples()
    {
        Assert.Equal(0, MathX.FSum([]));
        Assert.Equal(0, MathX.FSum([0]));
        Assert.Equal(13, MathX.FSum([13]));
        Assert.Equal(6, MathX.FSum([13, -7]));
        Assert.Equal(20000, MathX.FSum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e-100, MathX.FSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50]));
        Assert.Equal(1, MathX.FSum([3.000000000000002, -1.000000000000001, -1.000000000000001]));
        Assert.Equal(1, MathX.FSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1]));
        Assert.Equal(1, MathX.FSum([.23, .19, .17, .13, .11, .07, .05, .03, .02]));
    }

    [Fact]
    public void TwoSum_FastTwoSum_Are_Equal_Check()
    {
        var genNum = GenNumber(-1e123, 1e123, 1000);
        Gen.Select(genNum, genNum)
        .Where((a, b) => Math.Abs(a) >= Math.Abs(b))
        .Sample((a, b) => MathX.TwoSum(a, b) == MathX.FastTwoSum(a, b));
    }

    [Fact]
    public void TwoSum_Twice_Check()
    {
        var genNum = GenNumber(-1e123, 1e123, 1000);
        Gen.Select(genNum, genNum)
        .Sample((a, b) =>
        {
            var (hi, lo) = MathX.TwoSum(a, b);
            return hi + lo == hi
                && MathX.TwoSum(hi, lo) == (hi, lo)
                && MathX.TwoSum(lo, hi) == (hi, lo);
        });
    }

    [Fact]
    public void Shuffle_Check()
    {
        GenNumber(-1e123, 1e123, 1000).Array[2, 10]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original);
            var shuffledSum = MathX.FSum(shuffled);
            var originalMan = MathX.Mantissa(originalSum, out var originalExp);
            var shuffledMan = MathX.Mantissa(shuffledSum, out var shuffledExp);
            if (shuffledExp < originalExp)
                originalMan <<= originalExp - shuffledExp;
            else
                shuffledMan <<= shuffledExp - originalExp;
            return Math.Abs(originalMan - shuffledMan) <= 1;
        });
    }
}