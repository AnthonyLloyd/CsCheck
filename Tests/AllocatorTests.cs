namespace Tests;

using System;
using System.Linq;
using CsCheck;
using Xunit;

#nullable enable

public class AllocatorTests
{
    readonly static Gen<(long Total, double[] Weights)> genAllSigns =
        Gen.Select(Gen.Long[-100, 100], Gen.Double[-100, 100, 100].Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    readonly static Gen<(long Total, double[] Weights)> genIntegerWeights =
        Gen.Select(Gen.Long[-100, 100], Gen.Int[-100, 100].Cast<double>().Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    [Fact]
    public void ErrorMinimising_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_SmallerWeightsDontGetLargerAllocation()
        => AllocatorCheck.SmallerWeightsDontGetLargerAllocation(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_GivesOppositeForNegativeTotal()
        => AllocatorCheck.GivesOppositeForNegativeQuantity(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_GivesSameForNegativeWeights()
        => AllocatorCheck.GivesSameForNegativeWeights(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_GivesOppositeForNegativeBoth()
        => AllocatorCheck.GivesOppositeForNegativeBoth(genAllSigns, Allocator.Allocate);

    [Fact]
    public void ErrorMinimising_HasSmallestAllocationError()
        => AllocatorCheck.HasSmallestAllocationError(genAllSigns, Allocator.Allocate, true);

    [Fact]
    public void ErrorMinimising_HasSmallestAllocationError_ExactForIntegerWeights()
        => AllocatorCheck.HasSmallestAllocationError(genIntegerWeights, Allocator.Allocate, false);

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

    [Fact]
    public void ErrorMinimising_Integer()
    {
        Assert.Equal(new long[] { -3, 1, 1 }, Allocator.Allocate(-1, new[] { -24.0, 6.0, 8.0 }));
        Assert.Equal(new long[] { 0, 0, -1 }, Allocator.Allocate(-1, new[] { -2.0, -4.0, 16.0 }));
        Assert.Equal(new long[] { -2, -2, 3 }, Allocator.Allocate(-1, new[] { 31.0, 19.0, -38.0 }));
    }
}