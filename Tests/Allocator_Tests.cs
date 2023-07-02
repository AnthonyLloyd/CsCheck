namespace Tests;

using System;
using System.Linq;
using CsCheck;
using Xunit;

#nullable enable

public class Allocator_Tests
{
    readonly static Gen<(long Quantity, double[] Weights)> genAllSigns =
        Gen.Select(Gen.Long[-1000, 1000], Gen.Double[-100000, 100000, 10000].Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    readonly static Gen<(long Quantity, double[] Weights)> genPositive =
        Gen.Select(Gen.Long[0, 1000], Gen.Double[0, 100, 10].Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    [Fact]
    public void Allocate_TotalsCorrectly()
        => Allocator_Check.TotalsCorrectly(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_BetweenFloorAndCeiling()
        => Allocator_Check.BetweenFloorAndCeiling(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_SmallerWeightsDontGetLargerAllocation()
        => Allocator_Check.SmallerWeightsDontGetLargerAllocation(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesOppositeForNegativeTotal()
        => Allocator_Check.GivesOppositeForNegativeQuantity(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesSameForNegativeWeights()
        => Allocator_Check.GivesSameForNegativeWeights(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesOppositeForNegativeBoth()
        => Allocator_Check.GivesOppositeForNegativeBoth(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_HasSmallestAllocationError()
        => Allocator_Check.HasSmallestAllocationError(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_BalinskiYoung_TotalsCorrectly()
        => Allocator_Check.TotalsCorrectly(genPositive, Allocator.Allocate_BalinskiYoung);

    [Fact]
    public void Allocate_BalinskiYoung_BetweenFloorAndCeiling()
        => Allocator_Check.BetweenFloorAndCeiling(genPositive, Allocator.Allocate_BalinskiYoung);

    [Fact]
    public void Allocate_BalinskiYoung_SmallerWeightsDontGetLargerAllocation()
        => Allocator_Check.SmallerWeightsDontGetLargerAllocation(genPositive, Allocator.Allocate_BalinskiYoung);
    [Fact]
    public void Allocate_BalinskiYoung_NoAlabamaParadox()
        => Allocator_Check.NoAlabamaParadox(genPositive, Allocator.Allocate_BalinskiYoung);

    [Fact]
    public void Allocate_BalinskiYoung_MinErrorExample()
    {
        var actual = Allocator.Allocate_BalinskiYoung(10L, new[] { 10.0, 20.0, 30.0 });
        Assert.Equal(new long[] { 2, 3, 5 }, actual);
    }

    [Fact]
    public void Allocate_Twitter()
    {
        var actual = Allocator.Allocate(100L, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterZero()
    {
        var actual = Allocator.Allocate(0L, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 0, 0, 0, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterTotalNegative()
    {
        var actual = Allocator.Allocate(-100L, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterWeightsNegative()
    {
        var actual = Allocator.Allocate(100L, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterBothNegative()
    {
        var actual = Allocator.Allocate(-100L, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterTricky()
    {
        var actual = Allocator.Allocate(100L, new[] { 404.0, 397.0, 57.0, 57.0, 57.0, 28.0 });
        Assert.Equal(new long[] { 40, 39, 6, 6, 6, 3 }, actual);
    }

    [Fact]
    public void Allocate_NegativeExample()
    {
        var positive = Allocator.Allocate(42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        var negative = Allocator.Allocate(-42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(positive, Array.ConvertAll(negative, i => -i));
    }

    [Fact]
    public void Allocate_Exceptions()
    {
        Assert.Throws<Exception>(() => Allocator.Allocate(0, new[] { 0.0, 0.0, 0.0 }));
        Assert.Throws<Exception>(() => Allocator.Allocate(42, new[] { 1.0, -2.0, 1.0, 1e-30 }));
    }

    [Fact]
    public void Allocate_Integer()
    {
        Assert.Equal(new long[] { -3, 1, 1 }, Allocator.Allocate(-1L, new[] { -24.0, 6.0, 8.0 }));
        Assert.Equal(new long[] { 0, 0, -1 }, Allocator.Allocate(-1L, new[] { -2.0, -4.0, 16.0 }));
        Assert.Equal(new long[] { -2, -2, 3 }, Allocator.Allocate(-1L, new[] { 31.0, 19.0, -38.0 }));
        Assert.Equal(new long[] { 1, 4 }, Allocator.Allocate(5L, new[] { 1.0, 9.0 }));
    }
}