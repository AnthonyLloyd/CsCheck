namespace Tests;

using System;
using System.Linq;

#nullable enable

public static class Allocator
{
    /// <summary>Pro-rata quantity by weights. Round to long using an error minimising algorithm. This guarantees a smaller weight never gets a larger allocation.</summary>
    public static long[] Allocate(long quantity, double[] weights)
    {
        var sumWeights = weights.NSum();
        var residual = quantity;
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = (long)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero);
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual != 0)
        {
            if (residual >= weights.Length || residual <= -weights.Length)
                throw new Exception($"Allocate numeric overflow, quantity={quantity}, weights={string.Join(',', weights)}, residual={residual}");
            if (sumWeights > 0 && residual < 0 || sumWeights < 0 && residual > 0)
            {
                sumWeights = -sumWeights;
                quantity = -quantity;
            }
            do
            {
                var maxError = double.Epsilon;
                var maxWeight = 0.0;
                var index = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    var error = quantity * weights[i] - results[i] * sumWeights;
                    if (error > maxError || error == maxError && Math.Abs(weights[i]) > maxWeight)
                    {
                        maxError = error;
                        maxWeight = Math.Abs(weights[i]);
                        index = i;
                    }
                }
                var increment = Math.Sign(residual);
                results[index] += increment;
                residual -= increment;
            } while (residual != 0);
        }
        return results;
    }

    /// <summary>Pro-rata quantity by weights. Round to long using an error minimising algorithm. This guarantees a smaller weight never gets a larger allocation.</summary>
    public static int[] Allocate(int quantity, double[] weights)
    {
        var sumWeights = weights.FSum();
        var residual = quantity;
        var results = new int[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = (int)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero);
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual != 0)
        {
            if (residual >= weights.Length || residual <= -weights.Length)
                throw new Exception($"Allocate numeric overflow, quantity={quantity}, weights={string.Join(',', weights)}, residual={residual}");
            if (sumWeights > 0 && residual < 0 || sumWeights < 0 && residual > 0)
            {
                sumWeights = -sumWeights;
                quantity = -quantity;
            }
            do
            {
                var maxError = double.Epsilon;
                var maxWeight = 0.0;
                var index = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    var error = quantity * weights[i] - results[i] * sumWeights;
                    if (error > maxError || error == maxError && Math.Abs(weights[i]) > maxWeight)
                    {
                        maxError = error;
                        maxWeight = Math.Abs(weights[i]);
                        index = i;
                    }
                }
                var increment = Math.Sign(residual);
                results[index] += increment;
                residual -= increment;
            } while (residual != 0);
        }
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
                if (a < w * q / sumWeights) // to keep to quota rule
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
}