using System;
using System.Linq;
using Google.OrTools.LinearSolver;

namespace Tests;

public static class AllocateMany
{
    public static long[][] Allocate(double[] p, long[] q, double[] w, int timeout = 300_000)
    {
        var n = Allocator.Allocate(q.Sum(), w);
        return AllocateCore(p, q, n, timeout);
    }

    public static long[][] Allocate(double[] p, long[] q, long[] n, int timeout = 300_000)
    {
        if (q.Sum() != n.Sum())
            throw new Exception($"{q.Sum()} != {n.Sum()}");
        return AllocateCore(p, q, n, timeout);
    }

    private static long[][] AllocateCore(double[] p, long[] q, long[] n, int timeout)
    {
        var solver = Solver.CreateSolver("SCIP");
        solver.SetNumThreads(Environment.ProcessorCount);
        solver.SetTimeLimit(timeout);
        var a = solver.MakeIntVarMatrix(q.Length, n.Length, 0, double.PositiveInfinity);
        var z = solver.MakeNumVar(double.NegativeInfinity, double.PositiveInfinity, "z");
        for (int i = 0; i < q.Length; i++)
        {
            LinearExpr sum_j = a[i, 0];
            for (int j = 1; j < n.Length; j++)
                sum_j += a[i, j];
            solver.Add(sum_j == q[i]);
        }
        for (int j = 0; j < n.Length; j++)
        {
            var n_j = n[j];
            LinearExpr sum_i = a[0, j];
            var price_j = p[0] / n_j * a[0, j];
            for (int i = 1; i < p.Length; i++)
            {
                sum_i += a[i, j];
                price_j += p[i] / n_j * a[i, j];
            }
            solver.Add(sum_i == n_j);
            solver.Add(price_j <= z);
        }
        solver.Minimize(z);
        var solverResult = solver.Solve();
        if (solverResult is not Solver.ResultStatus.OPTIMAL and not Solver.ResultStatus.FEASIBLE) throw new Exception($"{solverResult}");
        var results = new long[q.Length][];
        for (int i = 0; i < results.Length; i++)
        {
            var row = new long[n.Length];
            results[i] = row;
            for (int j = 0; j < row.Length; j++)
                row[j] = (long)a[i, j].SolutionValue();
        }
        if (results.Sum(r => r.Sum()) != q.Sum())
            throw new Exception($"{results.Sum(r => r.Sum())} != {q.Sum()}");
        return results;
    }
}