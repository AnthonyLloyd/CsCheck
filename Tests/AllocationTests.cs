namespace Tests;

using Xunit;
using CsCheck;
using System;
using System.Collections.Generic;

#nullable enable

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
            throw new Exception($"Numeric overflow, total={total}, sum weights={sumWeights}, residual={residual}");
        var increment = Math.Sign(residual);
        while (residual != 0)
        {
            var minAbsError = double.MaxValue;
            var minRelError = double.MaxValue;
            var maxWeightDir = double.MinValue;
            var minErrorIndex = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var required = total * weights[i];
                var actual = results[i] * sumWeights;
                var absErrorIncrease = Math.Abs(actual + increment * sumWeights - required) - Math.Abs(actual - required);
                var relErrorIncrease = absErrorIncrease / Math.Abs(required);
                var weightDir = increment * Math.Sign(total) * Math.Sign(sumWeights) * weights[i];
                if (absErrorIncrease < minAbsError
                    || (absErrorIncrease == minAbsError && (relErrorIncrease < minRelError || (relErrorIncrease == minRelError  && weightDir > maxWeightDir))))
                {
                    minAbsError = absErrorIncrease;
                    minRelError = relErrorIncrease;
                    maxWeightDir = weightDir;
                    minErrorIndex = i;
                }
            }
            results[minErrorIndex] += increment;
            residual -= increment;
        }
        return results;
    }

    public static long[] AllocateFloor(long total, double[] weights)
    { // Floor can't be made to work as it doesn't do NegativeExample properly
        var sumWeights = Sum(weights);
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            results[i] = (long)Math.Floor(total * weights[i] / sumWeights);
        var residual = total - Sum(results);
        if (residual > results.Length || residual < 0)
            throw new Exception($"Numeric overflow, total={total}, sum weights={sumWeights}");
        while (residual != 0)
        {
            var minAbsError = double.MaxValue;
            var minRelError = double.MaxValue;
            var minErrorIndex = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var required = total * weights[i];
                var actual = results[i] * sumWeights;
                var absErrorIncrease = Math.Abs(actual + sumWeights - required) - Math.Abs(actual - required);
                var relErrorIncrease = absErrorIncrease / Math.Abs(required);
                if (absErrorIncrease < minAbsError || (absErrorIncrease == minAbsError && relErrorIncrease < minRelError))
                {
                    minAbsError = absErrorIncrease;
                    minRelError = relErrorIncrease;
                    minErrorIndex = i;
                }
            }
            results[minErrorIndex]++;
            residual--;
        }
        return results;
    }

    public static long[][] Allocate(long[] totals, double[] weights)
    {
        var results = new long[totals.Length][];
        var sumTotals = Sum(totals);
        var residualWeights = Allocate(sumTotals, weights);
        // Set weights to the allocated values
        weights = new double[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = residualWeights[i];
        var sumWeights = sumTotals;
        var residualTotals = new long[totals.Length];
        for (int t = 0; t < totals.Length; t++)
        {
            var total = totals[t];
            var residualT = total;
            var resultsT = new long[weights.Length];
            results[t] = resultsT;
            for (int w = 0; w < weights.Length; w++)
            {
                var allocj = (long)Math.Floor(total * weights[w] / sumWeights);
                resultsT[w] = allocj;
                residualT -= allocj;
                residualWeights[w] -= allocj;
            }
            residualTotals[t] = residualT;
        }
        while (true)
        {
            var maxError = double.MinValue;
            var maxErrorRel = double.MinValue;
            var maxErrorT = -1;
            var maxErrorW = -1;
            for (int t = 0; t < totals.Length; t++)
            {
                if (residualTotals[t] == 0) continue;
                var resultsT = results[t];
                var total = totals[t];
                for (int w = 0; w < weights.Length; w++)
                {
                    if (residualWeights[w] != 0)
                    {
                        var required = total * weights[w];
                        var error = required - resultsT[w] * sumWeights;
                        var errorRel = error / required;
                        if (error > maxError || (error == maxError && errorRel > maxErrorRel))
                        {
                            maxError = error;
                            maxErrorRel = errorRel;
                            maxErrorT = t;
                            maxErrorW = w;
                        }
                    }
                }
            }
            if (maxErrorT < 0) break;
            results[maxErrorT][maxErrorW]++;
            residualTotals[maxErrorT]--;
            residualWeights[maxErrorW]--;
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
        var positive = Allocate(42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        var negative = Allocate(-42, new[] { 1.5, 1.0, 39.5, -1.0, 1.0 });
        Assert.Equal(positive, Array.ConvertAll(negative, i => -i));
    }

    [Fact]
    public void SmallTotalWeight()
    {
        Assert.Throws<Exception>(() => Allocate(42, new[] { 1.0, -2.0, 1.0, 1e-30 }));
    }

    readonly static Gen<double> genDouble =
        Gen.Select(Gen.Int[-100, 100], Gen.Int[-100, 100], Gen.Int[1, 100])
        .Select((a, b, c) => a + (double)b / c);

    readonly static Gen<(int Total, double[] Weights)> genAllocateExample =
        Gen.Select(Gen.Int[-100, 100], genDouble.Array[1, 30])
        .Where((_, weights) => Math.Abs(Sum(weights)) > 1e-9);

    [Fact]
    public void AllocateTotalsCorrectly()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocations = Allocate(total, weights);
            return Sum(allocations) == total;
        });
    }

    [Fact]
    public void AllocateGivesOppositeForNegativeTotal()
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
    public void AllocateGivesSameForNegativeWeight()
    {
        genAllocateExample.Sample((total, weights) =>
        {
            var allocationsPositive = Allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = Allocate(total, negativeWeights);
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
            var allocationsPositive = Allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = Allocate(-total, negativeWeights);
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
            var sumWeights = Sum(weights);
            var allocations = Allocate(total, weights);
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
