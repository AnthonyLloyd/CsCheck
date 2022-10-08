using System;
using System.Collections.Generic;
using System.Linq;
using Google.OrTools.LinearSolver;

#nullable enable

namespace Tests;

public static class AllocateMany
{
    public static long[][] Allocate(double[] p, long[] q, double[] w, int timeout = 600_000)
    {
        var n = Allocator.Allocate(q.Sum(), w);
        return AllocateCore(p, q, n, timeout);
    }

    public static long[][] Allocate(double[] p, long[] q, long[] n, int timeout = 600_000)
    {
        if (q.Sum() != n.Sum())
            throw new Exception($"{q.Sum()} != {n.Sum()}");
        return AllocateCore(p, q, n, timeout);
    }

    private static long[][] AllocateCore(double[] p, long[] q, long[] n, int timeout)
    {
        var dedup = new Dictionary<double, long>(p.Length);
        int i = 0;
        for (; i < p.Length; i++)
        {
            var p_i = p[i];
            if (dedup.TryGetValue(p_i, out var d))
                dedup[p_i] = d + q[i];
            else
                dedup[p_i] = q[i];
        }
        var solver = Solver.CreateSolver("SCIP");
        solver.SetTimeLimit(timeout);
        //solver.SetNumThreads(16);
        //var b = solver.SetSolverSpecificParametersAsString("parallel/mode=0\nparallel/minnthreads=16\nparallel/maxnthreads=16\nlimits/time=180\n");
        //if (!b) throw new Exception("aaa");
        var a = solver.MakeIntVarMatrix(dedup.Count, n.Length, 0, double.PositiveInfinity);
        var z = solver.MakeNumVar(double.NegativeInfinity, double.PositiveInfinity, "");
        i = 0;
        foreach (var q_i in dedup.Values)
        {
            LinearExpr sum_j = a[i, 0];
            for (int j = 1; j < n.Length; j++)
                sum_j += a[i, j];
            solver.Add(sum_j == q_i);
            i++;
        }
        for (int j = 0; j < n.Length; j++)
        {
            var n_j = n[j];
            LinearExpr? sum_i = null;
            LinearExpr? price_j = null;
            i = 0;
            foreach (var p_i in dedup.Keys)
            {
                if (sum_i is null)
                {
                    sum_i = a[0, j];
                    price_j = p_i / n_j * a[0, j];
                }
                else
                {
                    sum_i += a[i, j];
                    price_j += p_i / n_j * a[i, j];
                }
                i++;
            }
            solver.Add(sum_i == n_j);
            solver.Add(price_j <= z);
        }
        solver.Minimize(z);
        var solverResult = solver.Solve();
        if (solverResult is not Solver.ResultStatus.OPTIMAL and not Solver.ResultStatus.FEASIBLE)
        {
            solver.Dispose();
            throw new Exception($"{solverResult}");
        }
        var results = new long[p.Length][];
        if (p.Length == dedup.Count)
        {
            for (i = 0; i < results.Length; i++)
            {
                var row = new long[n.Length];
                results[i] = row;
                for (int j = 0; j < row.Length; j++)
                    row[j] = (long)a[i, j].SolutionValue();
            }
            solver.Dispose();
        }
        else
        {
            var aggResults = new Dictionary<double, long[]>(dedup.Count);
            i = 0;
            foreach(var p_i in dedup.Keys)
            {
                var row = new long[n.Length];
                aggResults.Add(p_i, row);
                for (int j = 0; j < row.Length; j++)
                    row[j] = (long)a[i, j].SolutionValue();
                i++;
            }
            solver.Dispose();
            for (i = 0; i < p.Length; i++)
            {
                var p_i = p[i];
                var row = aggResults[p_i];
                var q_i = q[i];
                if (dedup[p_i] == q_i)
                    results[i] = row;
                else
                {
                    results[i] = TryTakeWhole(q_i, row);
                    dedup[p_i] -= q_i;
                }
            }
        }

        for (i = 0; i < results.Length; i++)
        {
            if (results[i].Sum() != q[i])
                throw new Exception($"Row {i} doesn't add up: {results[i].Sum()} != {q[i]}");
        }

        for (int j = 0; j < n.Length; j++)
        {
            var sum = 0L;
            for (i = 0; i < results.Length; i++)
                sum += results[i][j];
            if (sum != n[j])
                throw new Exception($"Column {j} doesn't add up: {sum} != {n[j]}");
        }

        return results;
    }

    private static long[] TryTakeWhole(long q, long[] source)
    {
        if (q > source.Sum()) throw new Exception($"{q} > {source.Sum()}");
        var result = new long[source.Length];
        start:
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == q)
            {
                source[i] = 0;
                result[i] = q;
                return result;
            }
        }
        for (int i = 0; i < source.Length; i++)
        {
            var source_i = source[i];
            if (source_i != 0 && source_i < q)
            {
                source[i] = 0;
                result[i] = source_i;
                q -= source_i;
                goto start;
            }
        }
        for (int i = 0; i < source.Length; i++)
        {
            var source_i = source[i];
            if (source_i != 0)
            {
                source[i] = source_i - q;
                result[i] = q;
                return result;
            }
        }
        goto start;
    }
}