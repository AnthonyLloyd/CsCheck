namespace Tests;

using System;
using System.Linq;

public static class Allocator
{
    /// <summary>Pro-rata quantity by weights. Round to long using an error minimising algorithm. This guarantees a smaller weight never gets a larger allocation.</summary>
    public static long[] Allocate(long quantity, double[] weights)
    {
        var sumWeights = weights.FSum(compress: true);
        var residual = quantity;
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = (long)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero);
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual == 0) return results;
        if (residual >= weights.Length || residual <= -weights.Length)
            throw new Exception($"Allocate numeric overflow, quantity={quantity}, weights={string.Join(',', weights)}, residual={residual}");
        if (residual * sumWeights < 0)
        {
            quantity = -quantity;
            sumWeights = -sumWeights;
        }
        do
        {
            var minError = double.MaxValue;
            var maxWeight = 0.0;
            var index = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var error = results[i] * sumWeights - quantity * weights[i];
                if (error < minError || error == minError && Math.Abs(weights[i]) > maxWeight)
                {
                    minError = error;
                    maxWeight = Math.Abs(weights[i]);
                    index = i;
                }
            }
            var increment = Math.Sign(residual);
            results[index] += increment;
            residual -= increment;
        } while (residual != 0);
        return results;
    }

    /// <summary>Pro-rata quantity by weights. Round to long using an error minimising algorithm. This guarantees a smaller weight never gets a larger allocation.</summary>
    public static int[] Allocate(int quantity, double[] weights)
    {
        var sumWeights = weights.FSum(compress: true);
        var residual = quantity;
        var results = new int[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = checked ((int)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero));
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual == 0) return results;
        if (residual >= weights.Length || residual <= -weights.Length)
            throw new Exception($"Allocate numeric overflow, quantity={quantity}, weights={string.Join(',', weights)}, residual={residual}");
        if (residual * sumWeights < 0)
        {
            quantity = -quantity;
            sumWeights = -sumWeights;
        }
        do
        {
            var minError = double.MaxValue;
            var maxWeight = 0.0;
            var index = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var error = results[i] * sumWeights - quantity * weights[i];
                if (error < minError || error == minError && Math.Abs(weights[i]) > maxWeight)
                {
                    minError = error;
                    maxWeight = Math.Abs(weights[i]);
                    index = i;
                }
            }
            var increment = Math.Sign(residual);
            results[index] += increment;
            residual -= increment;
        } while (residual != 0);
        return results;
    }

    public static long[] Allocate(long quantity, long[] weights)
    {
        var sumWeights = 0L;
        foreach (var weight in weights)
            sumWeights += weight;
        var residual = quantity;
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = Math.DivRem(checked(quantity * weights[i]), sumWeights, out var remainder) + remainder * 2 / sumWeights;
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual == 0) return results;
        if (residual * sumWeights < 0)
        {
            quantity = -quantity;
            sumWeights = -sumWeights;
        }
        do
        {
            var minError = long.MaxValue;
            var maxWeight = 0L;
            var index = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var error = results[i] * sumWeights - quantity * weights[i];
                if (error < minError || error == minError && Math.Abs(weights[i]) > maxWeight)
                {
                    minError = error;
                    maxWeight = Math.Abs(weights[i]);
                    index = i;
                }
            }
            var increment = Math.Sign(residual);
            results[index] += increment;
            residual -= increment;
        } while (residual != 0);
        return results;
    }

    public static long[] Allocate_BalinskiYoung(long quantity, double[] weights)
    {
        var sumWeights = weights.Sum();
        var allocations = new long[weights.Length];
        for (var q = 1L; q <= quantity; q++)
        {
            var rmax = double.MinValue;
            var amin = long.MaxValue;
            var index = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                var a = allocations[i];
                var w = weights[i];
                if (sumWeights * a < w * q) // to keep to quota rule
                {
                    var r = w / (1 + a); // divisor method
                    if (r > rmax || (r == rmax && a < amin))
                    {
                        rmax = r;
                        amin = a;
                        index = i;
                    }
                }
            }
            allocations[index]++;
        }
        return allocations;
    }

    public static long[] Allocate_BalinskiYoung(long quantity, long[] weights)
    {
        var sumWeights = weights.Sum();
        var allocations = new long[weights.Length];
        for (var q = 1L; q <= quantity; q++)
        {
            var rmax = double.MinValue;
            var amin = long.MaxValue;
            var index = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                var a = allocations[i];
                var w = weights[i];
                if (sumWeights * a < w * q) // to keep to quota rule
                {
                    var r = (double)w / (1 + a); // divisor method
                    if (r > rmax || (r == rmax && a < amin))
                    {
                        rmax = r;
                        amin = a;
                        index = i;
                    }
                }
            }
            allocations[index]++;
        }
        return allocations;
    }
}