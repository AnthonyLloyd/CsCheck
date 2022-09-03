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
        var results = new long[weights.Length];
        var residual = quantity;
        for (int i = 0; i < weights.Length; i++)
        {
            var allocation = (long)Math.Round(quantity * weights[i] / sumWeights, MidpointRounding.AwayFromZero);
            results[i] = allocation;
            residual -= allocation;
        }
        if (residual >= results.Length || residual <= -results.Length)
            throw new Exception($"Numeric overflow, quantity={quantity}, weights={string.Join(',', weights)}, residual={residual}");
        if (Math.Sign(sumWeights) * Math.Sign(residual) == -1)
        {
            sumWeights = -sumWeights;
            quantity = -quantity;
        }
        while (residual != 0)
        {
            var maxError = double.Epsilon;
            var maxWeight = 0.0;
            var index = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var weight = weights[i];
                var error = quantity * weight - results[i] * sumWeights;
                if (error < maxError) continue;
                var wei = Math.Abs(weight);
                if (error == maxError && wei <= maxWeight) continue;
                maxError = error;
                maxWeight = wei;
                index = i;
            }
            results[index] += Math.Sign(residual);
            residual -= Math.Sign(residual);
        }
        return results;
    }
}