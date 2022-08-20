namespace Tests;

using System;
using System.Collections.Generic;
using Microsoft.Z3;

#nullable enable

public static class Allocator
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

    static double AbsoluteWeightErrorChange(long total, double weight, double sumWeights, long n, int increment)
    {
        return Math.Abs((n + increment) * sumWeights / total - weight) - Math.Abs(n * sumWeights / total - weight);
    }

    private static void AllocateResidual(long total, double[] weights, double sumWeights, IList<long> results, long residual)
    {
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
                var abs = AbsoluteWeightErrorChange(total, weight, sumWeights, results[i], increment);
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
        AllocateResidual(total, weights, sumWeights, results, residual);
        return results;
    }

    public static long[][] Allocate(long[] totals, double[] weights)
    {
        var results = new long[totals.Length][];
        var sumTotals = Sum(totals);
        var sumWeights = Sum(weights);
        var residualWeights = Allocate(sumTotals, weights);
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
                    if (residualWeights[w] == 0) continue;
                    var result = resultsT[w];
                    if ((long)Math.Ceiling(total * weights[w] / sumWeights) == result) continue;
                    var weight = weights[w];
                    var abs = AbsoluteWeightErrorChange(total, weight, sumWeights, resultsT[w], 1);
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
            if (minIndexT == -1) break;
            results[minIndexT][minIndexW]++;
            residualTotals[minIndexT]--;
            residualWeights[minIndexW]--;
        }
        return results;
    }

    public static long[] BalinskiYoung(long total, double[] weights)
    {
        var sumWeights = Sum(weights);
        var results = new long[weights.Length];
        long h = 0;
        while (h++ != total)
        {
            var max = double.MinValue;
            var wei = double.MinValue;
            var index = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                var r = results[i];
                var w = weights[i];
                if (r < w * h / sumWeights)
                {
                    var v = w / (1 + r);
                    if (v > max || (v == max && w > wei))
                    {
                        max = v;
                        wei = w;
                        index = i;
                    }
                }
            }
            results[index]++;
        }
        return results;
    }

    public static long[][] BalinskiYoung(long total, double[][] weights)
    {
        var sumWeights = 0.0;
        for (int i = 0; i < weights.Length; i++)
            sumWeights += Sum(weights[i]);
        var results = new long[weights.Length][];
        for (int i = 0; i < weights.Length; i++)
            results[i] = new long[weights[i].Length];
        long h = 0;
        while (h++ != total)
        {
            var max = double.MinValue;
            var wei = double.MinValue;
            var indexI = -1;
            var indexJ = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                var resultsI = results[i];
                var weightsI = weights[i];
                for (int j = 0; j < weightsI.Length; j++)
                {
                    var r = resultsI[j];
                    var w = weightsI[j];
                    if (r < w * h / sumWeights)
                    {
                        var v = w / (1 + r);
                        if (v > max || (v == max && w > wei))
                        {
                            max = v;
                            wei = w;
                            indexI = i;
                            indexJ = j;
                        }
                    }
                }
            }
            results[indexI][indexJ]++;
        }
        return results;
    }

    public static long[][] BalinskiYoung(long[] totals, double[] weights)
    {
        var results = new long[totals.Length][];
        var sumWeights = Sum(weights);
        var cumulativeTotal = new long[weights.Length];
        var h = 0L;
        var total = 0L;
        var t = 0;
        while (true)
        {
            total += totals[t];
            while (h != total)
            {
                h++;
                var max = double.MinValue;
                var wei = double.MinValue;
                var index = -1;
                for (int i = 0; i < weights.Length; i++)
                {
                    var r = cumulativeTotal[i];
                    var w = weights[i];
                    if (r < w * h / sumWeights)
                    {
                        var v = w / (1 + r);
                        if (v > max || (v == max && w > wei))
                        {
                            max = v;
                            wei = w;
                            index = i;
                        }
                    }
                }
                cumulativeTotal[index]++;
            }
            if (t < totals.Length - 1)
            {
                results[t++] = (long[])cumulativeTotal.Clone();
            }
            else
            {
                results[t] = cumulativeTotal;
                break;
            }
        }
        t = results.Length - 1;
        var next = results[t];
        for (; t > 0; t--)
        {
            var resultsT1 = results[t - 1];
            for (int i = 0; i < next.Length; i++)
                next[i] -= resultsT1[i];
            next = resultsT1;
        }
        return results;
    }

    public static bool TryAllocate(long[] totals, double[] weights, out long[][] results)
    {
        results = new long[totals.Length][];
        var sumTotals = Sum(totals);
        var sumWeights = Sum(weights);
        var residualWeights = Allocate(sumTotals, weights);
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

        using var ctx = new Context();
        var bools = new BoolExpr[residualTotals.Length][];
        var i = 0;
        for (var t = 0; t < residualTotals.Length; t++)
        {
            var bools_t = new BoolExpr[residualWeights.Length];
            for (var w = 0; w < residualWeights.Length; w++)
                bools_t[w] = ctx.MkBoolConst(ctx.MkSymbol(i++));
            bools[t] = bools_t;
        }
        var one = ctx.MkInt(1);
        var zero = ctx.MkInt(0);
        var eqns = new BoolExpr[residualTotals.Length + residualWeights.Length];
        for (var t = 0; t < residualTotals.Length; t++)
        {
            var bools_t = bools[t];
            var expr = (IntExpr)ctx.MkITE(bools_t[0], one, zero);
            for (var w = 1; w < residualWeights.Length; w++)
                expr = (IntExpr)ctx.MkITE(bools_t[w], expr + one, expr);
            eqns[t] = ctx.MkEq(expr, ctx.MkInt(residualTotals[t]));
        }
        for (var w = 0; w < residualWeights.Length; w++)
        {
            var expr = (IntExpr)ctx.MkITE(bools[0][w], one, zero);
            for (var t = 1; t < residualTotals.Length; t++)
                expr = (IntExpr)ctx.MkITE(bools[t][w], expr + one, expr);
            eqns[residualTotals.Length + w] = ctx.MkEq(expr, ctx.MkInt(residualWeights[w]));
        }
        IntExpr sumExpr = zero;
        for (var t = 0; t < residualTotals.Length; t++)
        {
            var resultsT = results[t];
            var total = totals[t];
            for (var w = 0; w < residualWeights.Length; w++)
                {
                var increase_tw = (int)(10000 * AbsoluteWeightErrorChange(total, weights[w], sumWeights, resultsT[w], 1));
                sumExpr = (IntExpr)ctx.MkITE(bools[t][w], sumExpr + ctx.MkInt(increase_tw), sumExpr);
            }
        }
        var optimize = ctx.MkOptimize();
        optimize.Assert(eqns);
        optimize.MkMinimize(sumExpr);
        if (optimize.Check() != Status.SATISFIABLE) return false;
        foreach (var constResult in optimize.Model.Consts)
        {
            switch (((BoolExpr)constResult.Value).BoolValue)
            {
                case Z3_lbool.Z3_L_UNDEF:
                    return false;
                case Z3_lbool.Z3_L_TRUE:
                    {
                        var (t, w) = Math.DivRem(((IntSymbol)constResult.Key.Name).Int, weights.Length);
                        results[t][w]++;
                        break;
                    }
            }
        }
        return true;
    }
}

// https://qa.wujigu.com/qa/?qa=898054/c%23-proportionately-distribute-prorate-a-value-across-a-set-of-values
// https://stackoverflow.com/questions/62914824/c-sharp-split-integer-in-parts-given-part-weights-algorithm
// https://stackoverflow.com/questions/1925691/proportionately-distribute-prorate-a-value-across-a-set-of-values
// https://stackoverflow.com/questions/1925691/proportionately-distribute-prorate-a-value-across-a-set-of-values/1925719#1925719
// https://stackoverflow.com/questions/9088403/distributing-integers-using-weights-how-to-calculate
// https://stackoverflow.com/questions/792460/how-to-round-floats-to-integers-while-preserving-their-sum
// https://stackoverflow.com/questions/8685308/allocate-items-according-to-an-approximate-ratio-in-python
// https://en.wikipedia.org/wiki/Largest_remainder_method
// https://rangevoting.org/Apportion.html
// https://en.wikipedia.org/wiki/Apportionment_paradox