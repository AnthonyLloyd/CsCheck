namespace Tests;

using System;
using System.Linq;
using CsCheck;

public class MathXTests
{
    static readonly Gen<double> genDouble = Gen.Double[-1e123, 1e123];

    [Test]
    public void TwoSum_FastTwoSum_Are_Equal_Check()
    {
        Gen.Select(genDouble, genDouble)
        .Where((a, b) => Math.Abs(a) >= Math.Abs(b))
        .Sample((a, b) => MathX.TwoSum(a, b, out var err1) == MathX.FastTwoSum(a, b, out var err2) && err1 == err2);
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public async Task KSum_Examples()
    {
        await Assert.That(MathX.KSum([])).IsEqualTo(0);
        await Assert.That(MathX.KSum([0])).IsEqualTo(0);
        await Assert.That(MathX.KSum([13])).IsEqualTo(13);
        await Assert.That(MathX.KSum([13, -7])).IsEqualTo(6);
        await Assert.That(MathX.KSum([3.000000000000002, -1.000000000000001, -1.000000000000001])).IsEqualTo(1);
        await Assert.That(MathX.KSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1])).IsEqualTo(1);
        await Assert.That(MathX.KSum([.23, .19, .17, .13, .11, .07, .05, .03, .02])).IsEqualTo(1);
        await Assert.That(MathX.KSum([1, 1e100, 1, -1e100])).IsNotEqualTo(2); // reached it's accuracy tracking limit
        await Assert.That(MathX.KSum([10000, 1e104, 10000, -1e104])).IsNotEqualTo(20000);
        await Assert.That(MathX.KSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50])).IsNotEqualTo(1e-100);
    }

    [Test]
    public async Task NSum_Examples()
    {
        await Assert.That(MathX.NSum([])).IsEqualTo(0);
        await Assert.That(MathX.NSum([0])).IsEqualTo(0);
        await Assert.That(MathX.NSum([13])).IsEqualTo(13);
        await Assert.That(MathX.NSum([13, -7])).IsEqualTo(6);
        await Assert.That(MathX.NSum([3.000000000000002, -1.000000000000001, -1.000000000000001])).IsEqualTo(1);
        await Assert.That(MathX.NSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1])).IsEqualTo(1);
        await Assert.That(MathX.NSum([.23, .19, .17, .13, .11, .07, .05, .03, .02])).IsEqualTo(1);
        await Assert.That(MathX.NSum([1, 1e100, 1, -1e100])).IsEqualTo(2);
        await Assert.That(MathX.NSum([10000, 1e104, 10000, -1e104])).IsEqualTo(20000);
        await Assert.That(MathX.NSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50])).IsNotEqualTo(1e-100); // reached it's accuracy tracking limit
    }

    [Test]
    public async Task FSum_Examples()
    {
        await Assert.That(MathX.FSum([])).IsEqualTo(0);
        await Assert.That(MathX.FSum([0])).IsEqualTo(0);
        await Assert.That(MathX.FSum([13])).IsEqualTo(13);
        await Assert.That(MathX.FSum([13, -7])).IsEqualTo(6);
        await Assert.That(MathX.FSum([3.000000000000002, -1.000000000000001, -1.000000000000001])).IsEqualTo(1);
        await Assert.That(MathX.FSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1])).IsEqualTo(1);
        await Assert.That(MathX.FSum([.23, .19, .17, .13, .11, .07, .05, .03, .02])).IsEqualTo(1);
        await Assert.That(MathX.FSum([1, 1e100, 1, -1e100])).IsEqualTo(2);
        await Assert.That(MathX.FSum([10000, 1e104, 10000, -1e104])).IsEqualTo(20000);
        await Assert.That(MathX.FSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50])).IsEqualTo(1e-100);
    }

    [Test]
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

    [Test]
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

    [Test]
    public void KSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.KSum(original);
            var shuffledSum = MathX.KSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, TUnitX.WriteLine/*, time: 10*/);
    }

    [Test]
    public void NSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.NSum(original);
            var shuffledSum = MathX.NSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, TUnitX.WriteLine/*, time: 10*/);
    }

    [Test]
    public void FSum_Shuffle_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original);
            var shuffledSum = MathX.FSum(shuffled);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, TUnitX.WriteLine/*, time: 10*/);
    }

    [Test]
    public void FSum_Shuffle_Error_Distribution_Compress()
    {
        genDouble.Array[3, 100]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathX.FSum(original, compress: true);
            var shuffledSum = MathX.FSum(shuffled, compress: true);
            return Check.UlpsBetween(originalSum, shuffledSum).ToString().PadLeft(5);
        }, TUnitX.WriteLine/*, time: 10*/);
    }

  [Test]
    public async Task FSum_Compress_Needed_Example()
    {
        var weights = new double[] { -8485E-81, -68, 11d / 3, -5623E-76, 47E-55, -19, 88, 134E-33 };
        var shuffled = new double[] { -8485E-81, 47E-55, -19, 11d / 3, 134E-33, -5623E-76, 88, -68 };
        var weightsFSum = weights.FSum();
        var shuffledFSum = shuffled.FSum();
        await Assert.That(weightsFSum).IsNotEqualTo(shuffledFSum);
        var weightsFSumCompress = weights.FSum(compress: true);
        var shuffledFSumCompress = shuffled.FSum(compress: true);
        await Assert.That(weightsFSumCompress).IsEqualTo(shuffledFSumCompress);
    }

    [Test]
    public void NSum_FSum_Error_Distribution()
    {
        genDouble.Array[3, 100]
        .Sample(values =>
        {
            var fsumSum = MathX.FSum(values);
            var nsumSum = MathX.NSum(values);
            return Check.UlpsBetween(fsumSum, nsumSum).ToString().PadLeft(5);
        }, TUnitX.WriteLine/*, time: 10*/);
    }

    [Test]
    public async Task SSum_Examples()
    {
        await Assert.That(MathX.SSum([])).IsEqualTo(0);
        await Assert.That(MathX.SSum([0])).IsEqualTo(0);
        await Assert.That(MathX.SSum([13])).IsEqualTo(13);
        await Assert.That(MathX.SSum([7, 13, -7])).IsEqualTo(13);
        await Assert.That(MathX.SSum([3.000000000000002, -1.000000000000001, -1.000000000000001])).IsEqualTo(1);
        await Assert.That(MathX.SSum([.1, .1, .1, .1, .1, .1, .1, .1, .1, .1])).IsEqualTo(1);
        await Assert.That(MathX.SSum([.23, .19, .17, .13, .11, .07, .05, .03, .02])).IsEqualTo(1);
        await Assert.That(MathX.SSum([1, 1e100, 1, -1e100])).IsEqualTo(2);
        await Assert.That(MathX.SSum([10000, 1e104, 10000, -1e104])).IsEqualTo(20000);
        await Assert.That(MathX.SSum([1e100, 1, -1e100, 1e-100, 1e50, -1, -1e50])).IsEqualTo(1e-100);
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public void FSum_Vs_SSum_Perf()
    {
        genDouble.Array[2, 100]
        .Faster(
            values => values.FSum(),
            values => values.SSum(),
            Check.EqualSkip,
            writeLine: TUnitX.WriteLine
        );
    }

    [Test]
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