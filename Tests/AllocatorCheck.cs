namespace Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;

#nullable enable

internal static class AllocatorCheck
{
    public static void TotalsCorrectly(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) => allocate(quantity, weights).Sum() == quantity);
    }

    public static void BetweenFloorAndCeiling(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(quantity, weights);
            for (int i = 0; i < allocations.Length; i++)
            {
                var unrounded = quantity * weights[i] / sumWeights;
                if (allocations[i] < Math.Floor(unrounded) || allocations[i] > Math.Ceiling(unrounded))
                    return false;
            }
            return true;
        });
    }

    public static void GivesOppositeForNegativeQuantity(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) =>
        {
            var allocationsPositive = allocate(quantity, weights);
            var allocationsNegative = allocate(-quantity, weights);
            for (int i = 0; i < allocationsPositive.Length; i++)
            {
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            }

            return true;
        });
    }

    public static void GivesSameForNegativeWeights(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) =>
        {
            var allocationsPositive = allocate(quantity, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = allocate(quantity, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
            {
                if (allocationsPositive[i] != allocationsNegative[i])
                    return false;
            }

            return true;
        });
    }

    public static void GivesOppositeForNegativeBoth(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) =>
        {
            var allocationsPositive = allocate(quantity, weights);
            var negativeWeights = Array.ConvertAll(weights, i => -i);
            var allocationsNegative = allocate(-quantity, negativeWeights);
            for (int i = 0; i < allocationsPositive.Length; i++)
            {
                if (allocationsPositive[i] != -allocationsNegative[i])
                    return false;
            }

            return true;
        });
    }

    public static void SmallerWeightsDontGetLargerAllocation(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        gen.Sample((quantity, weights) =>
        {
            var allocations = allocate(quantity, weights);
            var comparer = weights.Sum() > 0.0 == quantity > 0
                ? Comparer<double>.Default
                : Comparer<double>.Create((x, y) => -x.CompareTo(y));
            Array.Sort(weights, allocations, comparer);
            var lastWeight = weights[0];
            var lastAllocation = allocations[0];
            for (int i = 1; i < weights.Length; i++)
            {
                var weight = weights[i];
                var allocation = allocations[i];
                if (weight != lastWeight && allocation < lastAllocation)
                    return false;
                lastWeight = weight;
                lastAllocation = allocation;
            }
            return true;
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

    public static void HasSmallestAllocationError(Gen<(long, double[])> gen, Func<long, double[], long[]> allocate)
    {
        static (double, double) Error(long[] allocations, long quantity, double[] weights, double sumWeights)
        {
            double errorAbs = 0, errorRel = 0;
            for (int i = 0; i < allocations.Length; i++)
            {
                var weight = weights[i];
                var error = Math.Abs(allocations[i] * sumWeights - quantity * weight);
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
        gen.Where((_, ws) => ws.Length >= 2)
        .SelectMany((quantity, weights) => GenChanges(weights.Length).Select(i => (quantity, weights, i)))
        .Sample((quantity, weights, changes) =>
        {
            var sumWeights = weights.Sum();
            var allocations = allocate(quantity, weights);
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
}