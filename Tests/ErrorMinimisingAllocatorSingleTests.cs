namespace Tests;

using Xunit;
using CsCheck;
using System;
using System.Linq;
using System.Collections.Generic;

#nullable enable

public class ErrorMinimisingAllocatorSingleTests
{
    readonly static Gen<double> genDouble =
        Gen.Select(Gen.Int[-100, 100], Gen.Int[-100, 100], Gen.Int[1, 100])
        .Select((a, b, c) => a + (double)b / c);

    readonly static Gen<(int Total, double[] Weights)> genAllocateExample =
        Gen.Select(Gen.Int[-100, 100], genDouble.Array[1, 30])
        .Where((_, weights) => Math.Abs(weights.Sum()) > 1e-9);

    [Fact]
    public void Twitter()
    {
        var actual = ErrorMinimisingAllocator.Allocate(100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void TwitterZero()
    {
        var actual = ErrorMinimisingAllocator.Allocate(0, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 0, 0, 0, 0 }, actual);
    }

    [Fact]
    public void TwitterTotalNegative()
    {
        var actual = ErrorMinimisingAllocator.Allocate(-100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void TwitterWeightsNegative()
    {
        var actual = ErrorMinimisingAllocator.Allocate(100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void TwitterBothNegative()
    {
        var actual = ErrorMinimisingAllocator.Allocate(-100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void TwitterTricky()
    {
        var actual = ErrorMinimisingAllocator.Allocate(100, new[] { 404.0, 397.0, 57.0, 57.0, 57.0, 28.0 });
        Assert.Equal(new long[] { 40, 39, 6, 6, 6, 3 }, actual);
    }

    [Fact]
    public void NegativeExample()
    {
        var positive = ErrorMinimisingAllocator.Allocate(42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        var negative = ErrorMinimisingAllocator.Allocate(-42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(positive, Array.ConvertAll(negative, i => -i));
    }

    [Fact]
    public void SmallTotalWeight()
    {
        Assert.Throws<Exception>(() => ErrorMinimisingAllocator.Allocate(42, new[] { 1.0, -2.0, 1.0, 1e-30 }));
    }

    [Fact]
    public void AllocateTotalsCorrectly()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocations = ErrorMinimisingAllocator.Allocate(total, weights);
            return allocations.Sum() == total;
        });
    }

    [Fact]
    public void AllocateGivesOppositeForNegativeTotal()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocationsPositive = ErrorMinimisingAllocator.Allocate(total, weights);
            var allocationsNegative = ErrorMinimisingAllocator.Allocate(-total, weights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            return true;
        });
    }

    [Fact]
    public void AllocateGivesSameForNegativeWeight()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocationsPositive = ErrorMinimisingAllocator.Allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = ErrorMinimisingAllocator.Allocate(total, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != allocationsNegative[i])
                    return false;
            return true;
        });
    }

    [Fact]
    public void AllocateGivesOppositeForNegativeBoth()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocationsPositive = ErrorMinimisingAllocator.Allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = ErrorMinimisingAllocator.Allocate(-total, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            return true;
        });
    }

    [Fact]
    public void AllocateIsFair_SmallerWeightsDontGetLargerAllocation()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocations = ErrorMinimisingAllocator.Allocate(total, weights);
            var comparer = weights.Sum() > 0.0 == total > 0
                ? Comparer<double>.Default
                : Comparer<double>.Create((x, y) => -x.CompareTo(y));
            Array.Sort(weights, allocations, comparer);
            var lastWeight = weights[0];
            var lastAllocation = allocations[0];
            for (int i = 1; i < weights.Length; i++)
            {
                var weight = weights[i];
                var allocation = allocations[i];
                if (weight != lastWeight)
                    if (allocation < lastAllocation)
                        return false;
                lastWeight = weight;
                lastAllocation = allocation;
            }
            return true;
        });
    }

    [Fact]
    public void AllocateHasSmallestError()
    {
        static double Error(long[] allocations, long total, double[] weights, double sumWeights)
        {
            double error = 0;
            for (int i = 0; i < allocations.Length; i++)
                error += Math.Abs(allocations[i] * sumWeights - total * weights[i]);
            return error;
        }
        static Gen<(int, int)[]> GenChanges(int i)
        {
            var genInt = Gen.Int[0, i - 1];
            return Gen.Select(genInt, genInt)
                .Where((i, j) => i != j)
                .Array[1, i];
        }
        genAllocateExample.Where(i => i.Weights.Length >= 2)
        .SelectMany(a => GenChanges(a.Weights.Length).Select(i => (a.Total, a.Weights, i)))
        .Sample((total, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = ErrorMinimisingAllocator.Allocate(total, weights);
            var errorBefore = Error(allocations, total, weights, sumWeights);
            foreach (var (i, j) in changes)
            {
                allocations[i]--;
                allocations[j]++;
            }
            var errorAfter = Error(allocations, total, weights, sumWeights);
            return errorAfter >= errorBefore || AreClose(errorAfter, errorBefore);
        });
    }

    static bool AreClose(double a, double b)
    {
        static double AreCloseLhs(double a, double b) => Math.Abs(a - b);
        static double AreCloseRhs(double a, double b) => 1e-12 + 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));
        return AreCloseLhs(a, b) <= AreCloseRhs(a, b);
    }
}