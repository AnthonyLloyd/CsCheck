namespace Tests;

using Xunit;
using CsCheck;
using System;
using System.Collections.Generic;

public class AllocationTests
{
    public static long[] Allocate(long total, double[] weights)
    {
        var sumWeights = Sum(weights);
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            results[i] = (long)Math.Round(total * weights[i] / sumWeights);
        var residual = total - Sum(results);
        if (residual > results.Length || residual < -results.Length)
            throw new Exception($"Numeric overflow, total={total}, sum weights={sumWeights}");
        var increment = Math.Sign(residual);
        while (residual != 0)
        {
            var minAbsError = double.MaxValue;
            var minRelError = double.MaxValue;
            var minErrorIndex = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var required = total * weights[i];
                var actual = results[i] * sumWeights;
                var absErrorIncrease = Math.Abs(actual + increment * sumWeights - required) - Math.Abs(actual - required);
                var relErrorIncrease = absErrorIncrease / Math.Abs(required);
                if (absErrorIncrease < minAbsError || (absErrorIncrease == minAbsError && relErrorIncrease < minRelError))
                {
                    minAbsError = absErrorIncrease;
                    minRelError = relErrorIncrease;
                    minErrorIndex = i;
                }
            }
            results[minErrorIndex] += increment;
            residual -= increment;
        }
        return results;
    }

    static double Sum(double[] array)
    {
        var sum = 0.0;
        for (int i = 0; i < array.Length; i++)
            sum += array[i];
        return sum;
    }

    static long Sum(long[] array)
    {
        var sum = 0L;
        for (int i = 0; i < array.Length; i++)
            sum += array[i];
        return sum;
    }

    // https://stackoverflow.com/questions/16226991/allocate-an-array-of-integers-proportionally-compensating-for-rounding-errors
    public static long[] AllocateButNotGood(long total, double[] weights)
    {
        var sumWeights = Sum(weights);
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length - 1; i++)
        {
            var w = weights[i];
            var r = (long)Math.Round(w / sumWeights * total, 0);
            sumWeights -= w;
            total -= r;
            results[i] = r;
        }
        results[^1] = total;
        return results;
    }

    [Fact]
    public void Twitter()
    {
        var actual = Allocate(100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void TwitterZero()
    {
        var actual = Allocate(0, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { 0, 0, 0, 0 }, actual);
    }

    [Fact]
    public void TwitterTotalNegative()
    {
        var actual = Allocate(-100, new[] { 406.0, 348.0, 246.0, 0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void TwitterWeightsNegative()
    {
        var actual = Allocate(100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { 40, 35, 25, 0 }, actual);
    }

    [Fact]
    public void TwitterBothNegative()
    {
        var actual = Allocate(-100, new[] { -406.0, -348.0, -246.0, -0.0 });
        Assert.Equal(new long[] { -40, -35, -25, -0 }, actual);
    }

    [Fact]
    public void TwitterTricky()
    {
        var actual = Allocate(100, new[] { 404.0, 397.0, 57.0, 57.0, 57.0, 28.0 });
        Assert.Equal(new long[] { 40, 39, 6, 6, 6, 3 }, actual);
    }

    [Fact]
    public void NegativeExample()
    {
        var actual = Allocate(42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(new long[] { 1, 1, 40, -1, 1 }, actual);
        actual = Allocate(-42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(new long[] { -1, -1, -40, 1, -1 }, actual);
    }

    [Fact]
    public void SmallTotalWeight()
    {
        Assert.Throws<Exception>(() => Allocate(42, new[] { 1.0, -2.0, 1.0, 1e-30 }));
    }

    readonly Gen<(int Total, double[] Weights)> genAllocateExample =
        Gen.Select(Gen.Int[-100, 100], Gen.Double[-100.0, 100.0].Array[1, 30]);

    [Fact]
    public void AllocationsTotalCorrectly()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocations = Allocate(total, weights);
            return Sum(allocations) == total;
        });
    }

    [Fact]
    public void NegativeTotalGivesOppositeOfPositiveTotal()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocationsPositive = Allocate(total, weights);
            var allocationsNegative = Allocate(-total, weights);
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
            var allocations = Allocate(total, weights);
            var comparer = Sum(weights) > 0.0 == total > 0
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
                error += Math.Abs(allocations[i] - total * weights[i] / sumWeights);
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
            var sumWeights = Sum(weights);
            var allocations = Allocate(total, weights);
            var errorBefore = Error(allocations, total, weights, sumWeights);
            foreach (var (i, j) in changes)
            {
                allocations[i]--;
                allocations[j]++;
            }
            var errorAfter = Error(allocations, total, weights, sumWeights);
            return errorAfter >= errorBefore;
        });
    }
}
