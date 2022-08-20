namespace Tests;

using Xunit;
using CsCheck;
using System;
using System.Linq;

#nullable enable

public class AllocatorTests
{
    readonly static Gen<(long Total, double[] Weights)> genAllocateAllSigns =
        Gen.Select(Gen.Long[-100, 100], Gen.Double[-100, 100, 100].Array[1, 30])
        .Where((_, weights) => Math.Abs(weights.Sum()) > 1e-9);

    [Fact]
    public void ErrorMinimising_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_SmallerWeightsDontGetLargerAllocation()
        => AllocatorCheck.SmallerWeightsDontGetLargerAllocation(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_GivesOppositeForNegativeTotal()
        => AllocatorCheck.GivesOppositeForNegativeTotal(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_GivesSameForNegativeWeights()
        => AllocatorCheck.GivesSameForNegativeWeights(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_GivesOppositeForNegativeBoth()
        => AllocatorCheck.GivesOppositeForNegativeBoth(genAllocateAllSigns, Allocator.Allocate);
    [Fact]
    public void ErrorMinimising_HasSmallestError()
        => AllocatorCheck.HasSmallestError(genAllocateAllSigns, Allocator.Allocate);
    [Fact(Skip ="ErrorMinimising doesn't solve the Alabama Paradox.")]
    public void ErrorMinimising_NoAlabamaParadox()
        => AllocatorCheck.NoAlabamaParadox(genAllocateAllSigns, Allocator.Allocate);

    readonly static Gen<(long Total, double[] Weights)> genAllocatePositive =
        Gen.Select(Gen.Long[0, 100], Gen.Double[0, 100, 100].Array[1, 30])
        .Where((_, weights) => Math.Abs(weights.Sum()) > 1e-9);

    [Fact]
    public void BalinskiYoung_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllocatePositive, Allocator.BalinskiYoung);
    [Fact]
    public void BalinskiYoung_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllocatePositive, Allocator.BalinskiYoung);
    [Fact]
    public void BalinskiYoung_SmallerWeightsDontGetLargerAllocation()
        => AllocatorCheck.SmallerWeightsDontGetLargerAllocation(genAllocatePositive, Allocator.BalinskiYoung);
    [Fact]
    public void BalinskiYoung_NoAlabamaParadox()
        => AllocatorCheck.NoAlabamaParadox(genAllocatePositive, Allocator.BalinskiYoung);
    [Fact(Skip = "BalinskiYoung doesn't always have the smallest error.")]
    public void BalinskiYoung_HasSmallestError()
        => AllocatorCheck.HasSmallestError(genAllocatePositive, Allocator.BalinskiYoung);

    readonly static Gen<(long[] Totals, double[] Weights)> genAllocateMany =
        Gen.Select(Gen.Long[1, 100].Array[1, 5], Gen.Double[0, 100, 100].Array[1, 5])
        .Where((_, weights) => Math.Abs(weights.Sum()) > 1e-9);

    [Fact(Skip = "Current algo gets itself stuck where it can't make the needed change to due to being already rounded in the direction.")]
    public void Many_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllocateMany, (totals, weights)  => Allocator.Allocate(totals, weights));
    [Fact]
    public void Many_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllocateMany, Allocator.Allocate);
    [Fact(Skip = "Current algo doesn't always have the smallest error.")]
    public void Many_HasSmallestError()
        => AllocatorCheck.HasSmallestError(genAllocateMany, (totals, weights) => Allocator.Allocate(totals, weights));

    [Fact]
    public void ManyBalinskiYoung_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllocateMany, Allocator.BalinskiYoung);
    [Fact(Skip = "BalinskiYoung not between floor and ceiling which is not great.")]
    public void ManyBalinskiYoung_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllocateMany, Allocator.BalinskiYoung);
    [Fact(Skip = "BalinskiYoung doesn't always have the smallest error.")]
    public void ManyBalinskiYoung_HasSmallestError()
        => AllocatorCheck.HasSmallestError(genAllocateMany, Allocator.BalinskiYoung);

    [Fact]
    public void ErrorMinimising_Twitter()
    {
        var actual = Allocator.Allocate(100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }
    [Fact]
    public void ErrorMinimising_TwitterZero()
    {
        var actual = Allocator.Allocate(0, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 0, 0, 0, 0 }, actual);
    }
    [Fact]
    public void ErrorMinimising_TwitterTotalNegative()
    {
        var actual = Allocator.Allocate(-100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }
    [Fact]
    public void ErrorMinimising_TwitterWeightsNegative()
    {
        var actual = Allocator.Allocate(100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }
    [Fact]
    public void ErrorMinimising_TwitterBothNegative()
    {
        var actual = Allocator.Allocate(-100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }
    [Fact]
    public void ErrorMinimising_TwitterTricky()
    {
        var actual = Allocator.Allocate(100, new[] { 404.0, 397.0, 57.0, 57.0, 57.0, 28.0 });
        Assert.Equal(new long[] { 40, 39, 6, 6, 6, 3 }, actual);
    }
    [Fact]
    public void ErrorMinimising_NegativeExample()
    {
        var positive = Allocator.Allocate(42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        var negative = Allocator.Allocate(-42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(positive, Array.ConvertAll(negative, i => -i));
    }
    [Fact]
    public void ErrorMinimising_SmallTotalWeight()
    {
        Assert.Throws<Exception>(() => Allocator.Allocate(42, new[] { 1.0, -2.0, 1.0, 1e-30 }));
    }
}