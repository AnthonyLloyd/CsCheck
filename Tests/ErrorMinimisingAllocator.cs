namespace Tests;

using System;

#nullable enable

public static class ErrorMinimisingAllocator
{
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

    static double AbsoluteErrorChange(double weight, long n, double sumWeights, long total, int increment)
    {
        var change = sumWeights / (increment * total);
        var weightn = sumWeights * n / total;
        return Math.Abs(weightn - weight + change) - Math.Abs(weightn - weight);
    }

    public static long[] Allocate(long total, double[] weights)
    {
        var sumWeights = Sum(weights);
        var results = new long[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            results[i] = (long)Math.Round(total * weights[i] / sumWeights);
        var residual = total - Sum(results);
        if (residual >= results.Length || residual <= -results.Length)
            throw new Exception($"Numeric overflow, total={total}, sum weights={sumWeights}, residual={residual}");
        var increment = Math.Sign(residual);
        while (residual != 0)
        {
            var minAbs = double.MaxValue;
            var minRel = double.MaxValue;
            var minWei = double.MaxValue;
            var minIndex = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                var weight = weights[i];
                var abs = AbsoluteErrorChange(weight, results[i], sumWeights, total, increment);
                var rel = abs / Math.Abs(weight);
                var wei = sumWeights > 0.0 ? -weight : weight;
                if (abs < minAbs || (abs == minAbs && (rel < minRel || (rel == minRel && wei < minWei))))
                {
                    minAbs = abs;
                    minRel = rel;
                    minWei = wei;
                    minIndex = i;
                }
            }
            results[minIndex] += increment;
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
        // Reset the weights to the allocated values
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
            var minAbs = double.MaxValue;
            var minRel = double.MaxValue;
            var minWei = double.MaxValue;
            var minIndexT = -1;
            var minIndexW = -1;
            for (int t = 0; t < totals.Length; t++)
            {
                if (residualTotals[t] == 0) continue;
                var resultsT = results[t];
                var total = totals[t];
                for (int w = 0; w < weights.Length; w++)
                {
                    if (residualWeights[w] != 0)
                    {
                        var weight = weights[w];
                        var abs = AbsoluteErrorChange(weight, resultsT[w], sumWeights, total, 1);
                        var rel = abs / Math.Abs(weight);
                        var wei = sumWeights > 0.0 ? -weight : weight;
                        if (abs < minAbs || (abs == minAbs && (rel < minRel || (rel == minRel && wei < minWei))))
                        {
                            minAbs = abs;
                            minRel = rel;
                            minWei = wei;
                            minIndexT = t;
                            minIndexW = w;
                        }
                    }
                }
            }
            if (minIndexT < 0) break;
            results[minIndexT][minIndexW]++;
            residualTotals[minIndexT]--;
            residualWeights[minIndexW]--;
        }
        return results;
    }
}
