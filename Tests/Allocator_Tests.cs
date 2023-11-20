namespace Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Rationals;
using Xunit;

public class Allocator_Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    readonly static Gen<(long Quantity, double[] Weights)> genAllSigns =
        Gen.Select(Gen.Long[-10_000, 10_000], Gen.Double[-10_000, 10_000].Array[2, 50].Where(ws => ws.Sum() > 1e-9));

    readonly static Gen<(long Quantity, long[] Weights)> genAllSignsLong =
        Gen.Select(Gen.Long[-10_000, 10_000], Gen.Long[-100_000, 100_000].Array[2, 50].Where(ws => ws.Sum() != 0));

    readonly static Gen<(long Quantity, double[] Weights)> genPositive =
        Gen.Select(Gen.Long[1, 10_000], Gen.Double[0, 10_000].Array[2, 50].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    readonly static Gen<(long Quantity, long[] Weights)> genPositiveLong =
        Gen.Select(Gen.Long[1, 10_000], Gen.Long[0, 100_000].Array[2, 50].Where(ws => ws.Sum() != 0));

    static bool TotalCorrectly<W>(long quantity, W[] weights, Func<long, W[], long[]> allocator)
        => allocator(quantity, weights).Sum() == quantity;

    [Fact]
    public void Allocate_TotalCorrectly()
    {
        genAllSigns.Sample((quantity, weights) => TotalCorrectly(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_TotalCorrectly()
    {
        genAllSignsLong.Sample((quantity, weights) => TotalCorrectly(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_BalinskiYoung_TotalsCorrectly()
    {
        genPositive.Sample((quantity, weights) => TotalCorrectly(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    [Fact]
    public void Allocate_BalinskiYoung_Long_TotalsCorrectly()
    {
        genPositiveLong.Sample((quantity, weights) => TotalCorrectly(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    static bool BetweenFloorAndCeiling<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate)
    {
        var sumWeights = weights.Sum(w => Convert.ToDouble(w));
        var allocations = allocate(quantity, weights);
        for (int i = 0; i < allocations.Length; i++)
        {
            var unrounded = quantity * Convert.ToDouble(weights[i]) / sumWeights;
            if (allocations[i] < Math.Floor(unrounded) || allocations[i] > Math.Ceiling(unrounded))
                return false;
        }
        return true;
    }

    [Fact]
    public void Allocate_BetweenFloorAndCeiling()
    {
        genAllSigns.Sample((quantity, weights) => BetweenFloorAndCeiling(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_BetweenFloorAndCeiling()
    {
        genAllSignsLong.Sample((quantity, weights) => BetweenFloorAndCeiling(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_BalinskiYoung_BetweenFloorAndCeiling()
    {
        genPositive.Sample((quantity, weights) => BetweenFloorAndCeiling(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    [Fact]
    public void Allocate_BalinskiYoung_Long_BetweenFloorAndCeiling()
    {
        genPositiveLong.Sample((quantity, weights) => BetweenFloorAndCeiling(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    static bool SmallerWeightsDontGetLargerAllocation<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate) where W : notnull
    {
        var allocations = allocate(quantity, weights);
        var comparer = weights.Sum(w => Convert.ToDouble(w)) > 0.0 == quantity > 0
            ? Comparer<W>.Default
            : Comparer<W>.Create((x, y) => -Comparer<W>.Default.Compare(x, y));
        Array.Sort(weights, allocations, comparer);
        var lastWeight = weights[0];
        var lastAllocation = allocations[0];
        for (int i = 1; i < weights.Length; i++)
        {
            var weight = weights[i];
            var allocation = allocations[i];
            if (!Equals(weight, lastWeight) && allocation < lastAllocation)
                return false;
            lastWeight = weight;
            lastAllocation = allocation;
        }
        return true;
    }

    [Fact]
    public void Allocate_SmallerWeightsDontGetLargerAllocation()
    {
        genAllSigns.Sample((quantity, weights) => SmallerWeightsDontGetLargerAllocation(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_SmallerWeightsDontGetLargerAllocation()
    {
        genAllSignsLong.Sample((quantity, weights) => SmallerWeightsDontGetLargerAllocation(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_BalinskiYoung_SmallerWeightsDontGetLargerAllocation()
    {
        genPositive.Sample((quantity, weights) => SmallerWeightsDontGetLargerAllocation(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    [Fact]
    public void Allocate_BalinskiYoung_Long_SmallerWeightsDontGetLargerAllocation()
    {
        genPositiveLong.Sample((quantity, weights) => SmallerWeightsDontGetLargerAllocation(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    static bool GivesSameResultReorderedForReorderedWeights<W>(long quantity, W[] weights, W[] shuffled, Func<long, W[], long[]> allocate)
    {
        var allocations = allocate(quantity, weights);
        var shuffledAllocations = allocate(quantity, shuffled);
        return weights.Zip(allocations).Order().Zip(shuffled.Zip(shuffledAllocations).Order()).All(i => Equals(i.First, i.Second));
    }

    [Fact]
    public void Allocate_GivesSameResultReorderedForReorderedWeights()
    {
        genAllSigns.SelectMany((q, w) => Gen.Shuffle(w).Select(s => (q, w, s)))
        .Sample((quantity, weights, shuffled) => GivesSameResultReorderedForReorderedWeights(quantity, weights, shuffled, Allocator.Allocate)
        , output.WriteLine);
    }

    [Fact]
    public void Allocate_Long_GivesSameResultReorderedForReorderedWeights()
    {
        genAllSignsLong.SelectMany((q, w) => Gen.Shuffle(w).Select(s => (q, w, s)))
        .Sample((quantity, weights, shuffled) => GivesSameResultReorderedForReorderedWeights(quantity, weights, shuffled, Allocator.Allocate)
        , output.WriteLine);
    }

    [Fact]
    public void Allocate_BalinskiYoung_GivesSameResultReorderedForReorderedWeights()
    {
        genPositive.SelectMany((q, w) => Gen.Shuffle(w).Select(s => (q, w, s)))
        .Sample((quantity, weights, shuffled) => GivesSameResultReorderedForReorderedWeights(quantity, weights, shuffled, Allocator.Allocate_BalinskiYoung)
        , output.WriteLine);
    }

    [Fact]
    public void Allocate_BalinskiYoung_Long_GivesSameResultReorderedForReorderedWeights()
    {
        genPositiveLong.SelectMany((q, w) => Gen.Shuffle(w).Select(s => (q, w, s)))
        .Sample((quantity, weights, shuffled) => GivesSameResultReorderedForReorderedWeights(quantity, weights, shuffled, Allocator.Allocate_BalinskiYoung)
        , output.WriteLine);
    }

    static bool GivesOppositeForNegativeQuantity<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate)
    {
        var allocationsPositive = allocate(quantity, weights);
        var allocationsNegative = allocate(-quantity, weights);
        for (int i = 0; i < allocationsPositive.Length; i++)
        {
            if (allocationsPositive[i] != -allocationsNegative[i])
                return false;
        }

        return true;
    }

    [Fact]
    public void Allocate_GivesOppositeForNegativeQuantity()
    {
        genAllSigns.Sample((quantity, weights) => GivesOppositeForNegativeQuantity(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_GivesOppositeForNegativeQuantity()
    {
        genAllSignsLong.Sample((quantity, weights) => GivesOppositeForNegativeQuantity(quantity, weights, Allocator.Allocate));
    }

    static W Negate<W>(W w)
    {
        if (w is double d) return (W)(object)-d;
        if (w is long l) return (W)(object)-l;
        throw new Exception();
    }

    static bool GivesSameForNegativeWeights<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate)
    {
        var allocationsPositive = allocate(quantity, weights);
        var negativeWeights = Array.ConvertAll(weights, Negate);
        var allocationsNegative = allocate(quantity, negativeWeights);
        for (int i = 0; i < allocationsPositive.Length; i++)
        {
            if (allocationsPositive[i] != allocationsNegative[i])
                return false;
        }

        return true;
    }

    [Fact]
    public void Allocate_GivesSameForNegativeWeights()
    {
        genAllSigns.Sample((quantity, weights) => GivesSameForNegativeWeights(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_GivesSameForNegativeWeights()
    {
        genAllSignsLong.Sample((quantity, weights) => GivesSameForNegativeWeights(quantity, weights, Allocator.Allocate));
    }

    static bool GivesOppositeForNegativeBoth<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate)
    {
        var allocationsPositive = allocate(quantity, weights);
        var negativeWeights = Array.ConvertAll(weights, Negate);
        var allocationsNegative = allocate(-quantity, negativeWeights);
        for (int i = 0; i < allocationsPositive.Length; i++)
        {
            if (allocationsPositive[i] != -allocationsNegative[i])
                return false;
        }

        return true;
    }

    [Fact]
    public void Allocate_GivesOppositeForNegativeBoth()
    {
        genAllSigns.Sample((quantity, weights) => GivesOppositeForNegativeBoth(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_Long_GivesOppositeForNegativeBoth()
    {
        genAllSignsLong.Sample((quantity, weights) => GivesOppositeForNegativeBoth(quantity, weights, Allocator.Allocate));
    }

    [Fact]
    public void Allocate_HasSmallestAllocationError()
    {
        static (double, double) Error(long[] allocations, long quantity, double[] weights, double sumWeights)
        {
            double errorAbs = 0, errorRel = 0;
            for (int i = 0; i < allocations.Length; i++)
            {
                var weight = weights[i];
                var error = Math.Abs(allocations[i] - weight / sumWeights * quantity);
                if (error == 0) continue;
                errorAbs += error;
                errorRel += error / Math.Abs(weight);
            }
            return (errorAbs, errorRel);
        }
        static Gen<HashSet<(int, int)>> GenChanges(int i)
        {
            var genInt = Gen.Int[0, i - 1];
            return Gen.Select(genInt, genInt)
                .Where((i, j) => i != j)
                .HashSet[1, i];
        }
        genAllSigns.Where((_, ws) => ws.Length >= 2)
        .SelectMany((quantity, weights) => GenChanges(weights.Length).Select(i => (quantity, weights, i)))
        .Sample((quantity, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = Allocator.Allocate(quantity, weights);
            var (errorBeforeAbs, errorBeforeRel) = Error(allocations, quantity, weights, sumWeights);
            foreach (var (i, j) in changes)
            {
                allocations[i]--;
                allocations[j]++;
            }
            var (errorAfterAbs, errorAfterRel) = Error(allocations, quantity, weights, sumWeights);
            return errorAfterAbs > errorBeforeAbs
                || Check.AreClose(1e-9, 1e-9, errorAfterAbs, errorBeforeAbs) && (errorAfterRel >= errorBeforeRel || Check.AreClose(1e-9, 1e-9, errorAfterRel, errorBeforeRel));
        });
    }

    [Fact]
    public void Allocate_Long_HasSmallestAllocationError()
    {
        static (long, Rational) Error(long[] allocations, long quantity, long[] weights, long sumWeights)
        {
            long errorAbs = 0L;
            var errorRel = Rational.Zero;
            for (int i = 0; i < allocations.Length; i++)
            {
                var weight = weights[i];
                var error = Math.Abs(allocations[i] * sumWeights - quantity * weight);
                if (error == 0) continue;
                errorAbs += error;
                errorRel += weight == 0 ? new Rational(10_000_000_000) : new Rational(error, Math.Abs(weight));
            }
            return (errorAbs, errorRel);
        }
        static Gen<HashSet<(int, int)>> GenChanges(int i)
        {
            var genInt = Gen.Int[0, i - 1];
            return Gen.Select(genInt, genInt)
                .Where((i, j) => i != j)
                .HashSet[1, i];
        }
        genAllSignsLong.Where((_, ws) => ws.Length >= 2)
        .SelectMany((quantity, weights) => GenChanges(weights.Length).Select(i => (quantity, weights, i)))
        .Sample((quantity, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = Allocator.Allocate(quantity, weights);
            var (errorBeforeAbs, errorBeforeRel) = Error(allocations, quantity, weights, sumWeights);
            foreach (var (i, j) in changes)
            {
                allocations[i]--;
                allocations[j]++;
            }
            var (errorAfterAbs, errorAfterRel) = Error(allocations, quantity, weights, sumWeights);
            return errorAfterAbs > errorBeforeAbs || errorAfterAbs == errorBeforeAbs && errorAfterRel >= errorBeforeRel;
        });
    }

    static bool NoAlabamaParadox<W>(long quantity, W[] weights, Func<long, W[], long[]> allocate)
    {
        var allocations = allocate(quantity, weights);
        var allocationsPlus = allocate(quantity + 1, weights);
        for (int i = 0; i < allocations.Length; i++)
            if (allocations[i] > allocationsPlus[i])
                return false;
        return true;
    }

    [Fact]
    public void Allocate_BalinskiYoung_NoAlabamaParadox()
    {
        genPositive.Sample((quantity, weights) => NoAlabamaParadox(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    [Fact]
    public void Allocate_BalinskiYoung_Long_NoAlabamaParadox()
    {
        genPositiveLong.Sample((quantity, weights) => NoAlabamaParadox(quantity, weights, Allocator.Allocate_BalinskiYoung));
    }

    [Fact]
    public void Allocate_BalinskiYoung_MinErrorExample()
    {
        var actual = Allocator.Allocate_BalinskiYoung(10L, [10.0, 20.0, 30.0]);
        Assert.Equal([2, 3, 5], actual);
    }

    [Fact]
    public void Allocate_Twitter()
    {
        var actual = Allocator.Allocate(100L, [406.0, 348.0, 246.0, 0.0]);
        Assert.Equal([40, 35, 25, 0], actual);
    }

    [Fact]
    public void Allocate_TwitterZero()
    {
        var actual = Allocator.Allocate(0L, [406.0, 348.0, 246.0, 0.0]);
        Assert.Equal([0, 0, 0, 0], actual);
    }

    [Fact]
    public void Allocate_TwitterTotalNegative()
    {
        var actual = Allocator.Allocate(-100L, [406.0, 348.0, 246.0, 0.0]);
        Assert.Equal([-40, -35, -25, -0], actual);
    }

    [Fact]
    public void Allocate_TwitterWeightsNegative()
    {
        var actual = Allocator.Allocate(100L, [-406.0, -348.0, -246.0, -0.0]);
        Assert.Equal([40, 35, 25, 0], actual);
    }

    [Fact]
    public void Allocate_TwitterBothNegative()
    {
        var actual = Allocator.Allocate(-100L, [-406.0, -348.0, -246.0, -0.0]);
        Assert.Equal([-40, -35, -25, -0], actual);
    }

    [Fact]
    public void Allocate_TwitterTricky()
    {
        var actual = Allocator.Allocate(100L, [404.0, 397.0, 57.0, 57.0, 57.0, 28.0]);
        Assert.Equal([40, 39, 6, 6, 6, 3], actual);
    }

    [Fact]
    public void Allocate_NegativeExample()
    {
        var positive = Allocator.Allocate(42, [1.5, 1.0, 39.5, -1.0, 1.0]);
        var negative = Allocator.Allocate(-42, [1.5, 1.0, 39.5, -1.0, 1.0]);
        Assert.Equal(positive, Array.ConvertAll(negative, i => -i));
    }

    [Fact]
    public void Allocate_Exceptions()
    {
        Assert.Throws<Exception>(() => Allocator.Allocate(0, [0.0, 0.0, 0.0]));
        Assert.Throws<Exception>(() => Allocator.Allocate(42, [1.0, -2.0, 1.0, 1e-30]));
    }

    [Fact]
    public void Allocate_Integer()
    {
        Assert.Equal([-3, 1, 1], Allocator.Allocate(-1L, [-24.0, 6.0, 8.0]));
        Assert.Equal([0, 0, -1], Allocator.Allocate(-1L, [-2.0, -4.0, 16.0]));
        Assert.Equal([-2, -2, 3], Allocator.Allocate(-1L, [31.0, 19.0, -38.0]));
        Assert.Equal([1, 4], Allocator.Allocate(5L, [1.0, 9.0]));
    }

    [Fact]
    public void Allocate_FSum_Needs_Compress_Example()
    {
        const long quantity = -35L;
        var weights = new double[] { -8485E-81, -68, 11d / 3, -5623E-76, 47E-55, -19, 88, 134E-33 };
        var shuffled = new double[] { -8485E-81, 47E-55, -19, 11d / 3, 134E-33, -5623E-76, 88, -68 };
        var allocations = Allocator.Allocate(quantity, weights);
        var shuffledAllocations = Allocator.Allocate(quantity, shuffled);
        Assert.True(weights.Zip(allocations).Order().Zip(shuffled.Zip(shuffledAllocations).Order()).All(i => i.First == i.Second));
    }
}