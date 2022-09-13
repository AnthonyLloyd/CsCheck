namespace Tests;

using System;
using System.Linq;
using CsCheck;
using Xunit;

#nullable enable

public class AllocatorTests
{
    readonly static Gen<(long Quantity, double[] Weights)> genAllSigns =
        Gen.Select(Gen.Long[-1000, 1000], Gen.Double[-100000, 100000, 10000].Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    [Fact]
    public void Allocate_TotalsCorrectly()
        => AllocatorCheck.TotalsCorrectly(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_BetweenFloorAndCeiling()
        => AllocatorCheck.BetweenFloorAndCeiling(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_SmallerWeightsDontGetLargerAllocation()
        => AllocatorCheck.SmallerWeightsDontGetLargerAllocation(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesOppositeForNegativeTotal()
        => AllocatorCheck.GivesOppositeForNegativeQuantity(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesSameForNegativeWeights()
        => AllocatorCheck.GivesSameForNegativeWeights(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_GivesOppositeForNegativeBoth()
        => AllocatorCheck.GivesOppositeForNegativeBoth(genAllSigns, Allocator.Allocate);

    [Fact]
    public void Allocate_HasSmallestAllocationErrorClose()
        => AllocatorCheck.HasSmallestAllocationErrorClose(genAllSigns, Allocator.Allocate);

    [Fact()]
    public void Allocate_HasSmallestAllocationErrorExact()
        => AllocatorCheck.HasSmallestAllocationErrorExact(genAllSigns, Allocator.Allocate);

    readonly static Gen<(long Quantity, double[] Weights)> genIntegerWeights =
        Gen.Select(Gen.Long[-1000, 1000], Gen.Int[-100000, 100000].Cast<double>().Array[1, 30].Where(ws => Math.Abs(ws.Sum()) > 1e-9));

    [Fact]
    public void Allocate_HasSmallestAllocationErrorClose_IntegerWeights()
        => AllocatorCheck.HasSmallestAllocationErrorClose(genIntegerWeights, Allocator.Allocate);

    [Fact]
    public void Allocate_HasSmallestAllocationErrorExact_IntegerWeights()
        => AllocatorCheck.HasSmallestAllocationErrorExact(genIntegerWeights, Allocator.Allocate);

    [Fact]
    public void Allocate_Twitter()
    {
        var actual = Allocator.Allocate(100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterZero()
    {
        var actual = Allocator.Allocate(0, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 0, 0, 0, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterTotalNegative()
    {
        var actual = Allocator.Allocate(-100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterWeightsNegative()
    {
        var actual = Allocator.Allocate(100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterBothNegative()
    {
        var actual = Allocator.Allocate(-100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void Allocate_TwitterTricky()
    {
        var actual = Allocator.Allocate(100, new[] { 404.0, 397.0, 57.0, 57.0, 57.0, 28.0 });
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
        Assert.Equal(new long[] { -3, 1, 1 }, Allocator.Allocate(-1, new[] { -24.0, 6.0, 8.0 }));
        Assert.Equal(new long[] { 0, 0, -1 }, Allocator.Allocate(-1, new[] { -2.0, -4.0, 16.0 }));
        Assert.Equal(new long[] { -2, -2, 3 }, Allocator.Allocate(-1, new[] { 31.0, 19.0, -38.0 }));
        Assert.Equal(new long[] { 1, 4 }, Allocator.Allocate(5, new[] { 1.0, 9.0 }));
    }
}