namespace Tests;

using System;

#nullable enable

public static class Allocator
{
    /// <summary>Pro-rata quantity by weights. Round to long using a minimum error algorithm. This guarantees a smaller weight never gets a larger allocation.</summary>
    public static long[] Allocate(long quantity, double[] weights)
    {
        var sumWeights = 0.0;
        for (int i = 0; i < weights.Length; i++)
            sumWeights += weights[i];
        var residual = quantity;
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = (long)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero);
            residual -= allocation;
            results[i] = allocation;
        }
        if (residual == 0)
            return results;
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
                if (error < maxError) continue;
                var weight = Math.Abs(weights[i]);
                if (error == maxError && weight <= maxWeight) continue;
                maxError = error;
                maxWeight = weight;
                index = i;
            }
            var increment = Math.Sign(residual);
            residual -= increment;
            results[index] += increment;
        } while (residual != 0);
        return results;
    }
}