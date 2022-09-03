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
        var increment = Math.Sign(residual);
        while (residual != 0)
        {
            var minInc = double.MaxValue;
            var maxWei = double.MinValue;
            var minIndex = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var weight = weights[i];
                // the following gives better handling of calculation rounding errors than:
                // allocation error:  var error = results[i] - quantity * weight / sumWeights;
                // weight error:      var error = results[i] * sumWeights / quantity - weight;
                // norm weight error: var error = results[i] / quantity - weight / sumWeights;
                var error = results[i] * sumWeights - quantity * weight;
                var inc = Math.Abs(error + increment * sumWeights) - Math.Abs(error); // increase in the error
                if (inc > minInc) continue;
                var wei = Math.Abs(weight);
                if (inc == minInc && wei <= maxWei) continue;
                minInc = inc;
                maxWei = wei;
                minIndex = i;
            }
            results[minIndex] += increment;
            residual -= increment;
        }
        return results;
    }
}

// 1 or 1-2f