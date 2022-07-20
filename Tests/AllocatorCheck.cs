namespace Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;

internal static class AllocatorCheck
{
    public static void TotalsCorrectly(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((total, weights) => allocate(total, weights).Sum() == total);
    }

    public static void TotalsCorrectly(Gen<(long[] Totals, double[] Weights)> gen, Func<long[], double[], long[][]> allocate)
    {
        gen.Sample((totals, weights) =>
        {
            foreach (var (allocs, total) in allocate(totals, weights).Zip(totals))
                if (allocs.Sum() != total)
                    return false;
            return true;
        }, seed: "bxR2SBs8lofc");
    }

    public static void GivesOppositeForNegativeTotal(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((total, weights) =>
        {
            var allocationsPositive = allocate(total, weights);
            var allocationsNegative = allocate(-total, weights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            return true;
        });
    }

    public static void GivesSameForNegativeWeights(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((total, weights) =>
        {
            var allocationsPositive = allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = allocate(total, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != allocationsNegative[i])
                    return false;
            return true;
        });
    }

    public static void GivesOppositeForNegativeBoth(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((total, weights) =>
        {
            var allocationsPositive = allocate(total, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = allocate(-total, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            return true;
        });
    }

    public static void SmallerWeightsDontGetLargerAllocation(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((total, weights) =>
        {
            var allocations = allocate(total, weights);
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

    public static void HasSmallestError(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
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
        gen.Where(i => i.Weights.Length >= 2)
        .SelectMany((total, weights) => GenChanges(weights.Length).Select(i => (total, weights, i)))
        .Sample((total, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(total, weights);
            var errorBefore = Error(allocations, total, weights, sumWeights);
            foreach (var (i, j) in changes)
            {
                allocations[i]--;
                allocations[j]++;
            }
            var errorAfter = Error(allocations, total, weights, sumWeights);
            return errorAfter >= errorBefore || Check.AreClose(1e-12, 1e-9, errorAfter, errorBefore);
        });
    }

    public static void HasSmallestError(Gen<(long[] Totals, double[] Weights)> gen, Func<long[], double[], long[][]> allocate)
    {
        static double Error(long[][] allocations, long[] totals, double[] weights, double sumWeights)
        {
            double error = 0;
            for (int t = 0; t < totals.Length; t++)
            {
                var total = totals[t];
                var allocationsT = allocations[t];
                for (int w = 0; w < weights.Length; w++)
                    error += Math.Abs(allocationsT[w] * sumWeights - total * weights[w]);
            }
            return error;
        }
        static Gen<(int T1, int W1, int T2, int W2)[]> GenChanges(int t, int w)
        {
            var genIntT = Gen.Int[0, t - 1];
            var genIntW = Gen.Int[0, w - 1];
            return Gen.Select(genIntT, genIntW, genIntT, genIntW)
                .Where((t1, w1, t2, w2) => t1 != t2 && w1 != w2)
                .Array[1, 8];
        }
        gen.Where(i => i.Totals.Length >= 2 && i.Weights.Length >= 2)
        .SelectMany((totals, weights) => GenChanges(totals.Length, weights.Length).Select(i => (totals, weights, i)))
        .Sample((totals, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(totals, weights);
            var errorBefore = Error(allocations, totals, weights, sumWeights);
            foreach (var (t1, w1, t2, w2) in changes)
            {
                allocations[t1][w1]--;
                allocations[t2][w1]++;
                allocations[t2][w2]--;
                allocations[t1][w2]++;
            }
            var errorAfter = Error(allocations, totals, weights, sumWeights);
            return errorAfter >= errorBefore || Check.AreClose(1e-12, 1e-9, errorAfter, errorBefore);
        });
    }

    public static void NoAlabamaParadox(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Where((total, weights) => total > 0 && weights.All(w => w >= 0))
        .Sample((total, weights) =>
        {
            var allocations = allocate(total, weights);
            var allocationsPlus = allocate(total + 1, weights);
            for (int i = 0; i < allocations.Length; i++)
                if (allocations[i] > allocationsPlus[i])
                    return false;
            return true;
        });
    }

    public static void BetweenFloorAndCeiling(Gen<(long Total, double[] Weights)> gen, Func<long, double[], long[]> allocate)
    {
        gen.Where((total, weights) => total > 0 && weights.All(w => w >= 0))
        .Sample((total, weights) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(total, weights);
            for (int i = 0; i < allocations.Length; i++)
            {
                var doubleAllocation = total * weights[i] / sumWeights;
                if (allocations[i] < Math.Floor(doubleAllocation) || allocations[i] > Math.Ceiling(doubleAllocation))
                    return false;
            }
            return true;
        });
    }

    public static void BetweenFloorAndCeiling(Gen<(long[] Totals, double[] Weights)> gen, Func<long[], double[], long[][]> allocate)
    {
        gen.Where((totals, weights) => totals.All(t => t >= 0) && weights.All(w => w >= 0))
        .Sample((totals, weights) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(totals, weights);
            for (int i = 0; i < allocations.Length; i++)
            {
                var total = totals[i];
                var allocationsI = allocations[i];
                for (int j = 0; j < weights.Length; j++)
                {
                    var doubleAllocation = total * weights[j] / sumWeights;
                    if (allocationsI[j] < Math.Floor(doubleAllocation) || allocationsI[j] > Math.Ceiling(doubleAllocation))
                        return false;
                }
            }
            return true;
        }, seed: "8-J0IDdNLP_5");
    }
}
