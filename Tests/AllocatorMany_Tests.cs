namespace Tests;

using System;
using System.Linq;
using System.Threading;
using CsCheck;

public class AllocatorMany_Tests
{
    [Test]
    public void RoundingSolutionTest()
    {
        Gen.Select(
            Gen.Int[1, 1000].Array[2, 200],
            Gen.Int[1, 1000].Select(i => (double)i).Array[2, 20])
        .Sample((rowTotal, colWeight) =>
        {
            var colTotal = Allocator.Allocate(rowTotal.Sum(), colWeight);
            var results = AllocatorMany.RoundingSolution(rowTotal, colTotal);
            return TotalsCorrectly(rowTotal, colTotal, results);
        });
    }

    [Test]
    public void GroupUngroup()
    {
        Gen.Select(Gen.Int[2, 10], Gen.Int[2, 10]).SelectMany((I, J) =>
            Gen.Select(
                Gen.Int[0, 5].Array[J].Where(a => a.Sum() > 0).Array[I],
                Gen.Int[0, 10].Array[I]))
        .Sample((solution,
                 rowPrice) =>
        {
            var rowTotal = Array.ConvertAll(solution, x => x.Sum());
            var rowGroup = AllocatorMany.GroupByPrice(rowPrice, rowTotal);
            if (rowGroup is null)
                return rowPrice.Distinct().Count() == rowPrice.Length;
            var solutionGroup = rowGroup.Keys.Select(price =>
            {
                var i = Array.IndexOf(rowPrice, price);
                var firstRow = solution[i];
                var i2 = Array.IndexOf(rowPrice, price, i + 1);
                if (i2 == -1)
                {
                    return firstRow;
                }
                else
                {
                    static void Add(int[] total, int[] row) { for (int i = 0; i < total.Length; i++) total[i] += row[i]; }
                    var total = (int[])firstRow.Clone();
                    Add(total, solution[i2]);
                    while ((i2 = Array.IndexOf(rowPrice, price, i2 + 1)) != -1)
                        Add(total, solution[i2]);
                    return total;
                }
            }).ToArray();
            var ungroup = AllocatorMany.UnGroupByPrice(rowPrice, rowTotal, rowGroup, solutionGroup);
            for (int i = 0; i < rowTotal.Length; i++)
                if (rowTotal[i] != ungroup[i].Sum())
                    return false;
            for (int j = solution[0].Length - 1; j >= 0; j--)
            {
                if (solution.SumCol(j) != ungroup.SumCol(j))
                    return false;
                var costSolution = 0;
                var costUngroup = 0;
                for (int i = 0; i < rowPrice.Length; i++)
                {
                    var price = rowPrice[i];
                    costSolution += price * solution[i][j];
                    costUngroup += price * ungroup[i][j];
                }
                if (costSolution != costUngroup)
                    return false;
            }
            return true;
        });
    }

    [Test]
    public async Task AllocatorMany_Classify()
    {
        Gen.Select(Gen.Int[3, 30], Gen.Int[3, 15]).SelectMany((rows, cols) =>
            Gen.Select(
                Gen.Int[0, 5].Array[cols].Where(a => a.Sum() > 0).Array[rows],
                Gen.Int[900, 1000].Array[rows],
                Gen.Int.Uniform))
        .Sample((solution,
                 rowPrice,
                 seed) =>
        {
            var rowTotal = Array.ConvertAll(solution, row => row.Sum());
            var colTotal = Enumerable.Range(0, solution[0].Length).Select(col => solution.SumCol(col)).ToArray();
            var allocation = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, new(seed), time: 1);
            if (!TotalsCorrectly(rowTotal, colTotal, allocation.Solution))
                throw new Exception("Does not total correctly");
            return $"{(allocation.KnownGlobal ? "Global" : "Local")}/{allocation.SolutionType}";
        }, writeLine: TUnitX.WriteLine, time: 10, threads: 1);
    }

    [Test]
    public async Task Simple()
    {
        var actual = AllocatorMany.Allocate([9, 2, 1], [1, 20, 20], [21, 0, 10, 0, 10], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.RoundingMinimum);
        await Assert.That(Check.Equal(actual.Solution, [[1, 0, 0, 0, 0], [6, 0, 7, 0, 7], [14, 0, 3, 0, 3]])).IsTrue();
    }

    [Test]
    public async Task Example01()
    {
        var actual = AllocatorMany.Allocate([96625, 96620], [4, 6], [4, 1, 1, 1, 1, 1, 1], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.EveryCombination);
        await Assert.That(Check.Equal(actual.Solution, [[3, 1, 0, 0, 0, 0, 0], [1, 0, 1, 1, 1, 1, 1]])).IsTrue();
    }

    [Test]
    public async Task Example02()
    {
        var actual = AllocatorMany.Allocate([12, 11], [50, 30], [20, 60], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.RoundingMinimum);
        await Assert.That(Check.Equal(actual.Solution, [[12, 38], [8, 22]])).IsTrue();
    }

    [Test]
    public async Task Example03()
    {
        var actual = AllocatorMany.Allocate([12, 11, 15], [56, 42, 14], [28, 63, 21], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.RoundingMinimum);
        await Assert.That(Check.Equal(actual.Solution, [[16, 31, 9], [9, 24, 9], [3, 8, 3]])).IsTrue();
    }

    [Test]
    public async Task Example04()
    {
        var actual = AllocatorMany.Allocate(
            [3175, 3174, 3173, 3170, 3169, 3168, 3167],
            [2, 3, 1, 1, 1, 2, 1],
            [4, 1, 1, 1, 1, 1, 1, 1], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.EveryCombination);
        await Assert.That(Check.Equal(actual.Solution, [
            [2, 0, 0, 0, 0, 0, 0, 0],
            [0, 1, 1, 1, 0, 0, 0, 0],
            [0, 0, 0, 0, 1, 0, 0, 0],
            [0, 0, 0, 0, 0, 1, 0, 0],
            [0, 0, 0, 0, 0, 0, 1, 0],
            [1, 0, 0, 0, 0, 0, 0, 1],
            [1, 0, 0, 0, 0, 0, 0, 0],
        ])).IsTrue();
    }

    [Test][Skip("Takes too long")]
    public async Task Example05()
    {
        var actual = AllocatorMany.Allocate(
            [37060, 37073, 37748, 38051],
            [586, 1055, 7183, 5560],
            [2744, 413, 524, 10582, 121], new(123), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.RandomChange);
    }

    [Test]
    public async Task Example06()
    {
        var actual = AllocatorMany.Allocate(
            [127584, 127678, 128097, 128157, 128483],
            [1, 1, 1, 1, 1],
            [0, 1, 1, 0, 0, 1, 0, 1, 1, 0], new(), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.OnesColumn);
    }

    [Test]
    public async Task Example07()
    {
        var actual = AllocatorMany.Allocate(
            [34378, 34506, 34535],
            [3900, 800, 400],
            [900, 400, 3800], new(123), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
    }

    [Test][Skip("fails in parallel")]
    public async Task Example08()
    {
        var actual = AllocatorMany.Allocate(
            [18880, 18916, 18920],
            [1861271, 61527, 67534],
            [381549, 56645, 69287, 1466297, 16554], new(123), 2);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.RandomChange);
    }

    [Test]
    public async Task Example09()
    {
        var actual = AllocatorMany.Allocate(
            [96030, 96050, 96040, 95055, 96040, 96035, 96035, 96035],
            [1, 1, 1, 1, 1, 1, 1, 1],
            [1, 5, 2], new(123), 100);
        await Assert.That(actual.KnownGlobal).IsTrue();
        await Assert.That(actual.SolutionType).IsEqualTo(AllocatorMany.SolutionType.EveryCombination);
        await Assert.That(Check.Equal(actual.Solution, [
            [1, 0, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 0, 1],
            [0, 0, 1],
        ])).IsTrue();
    }

    [Test][Skip("fails in parallel")]
    public async Task Example10()
    {
        Gen.Int[2, 20].SelectMany(rows =>
            Gen.Select(
                Gen.Int[1, 300].Array[rows],
                Gen.Int[1, 100].Array[rows],
                Gen.Int[1, 1000].Select(i => (double)i).Array[2, 10],
                Gen.Int.Uniform))
        .Sample((rowPrice, rowTotal, weight, seed) =>
        {
            var random = new Random(seed);
            var colTotal = Allocator.Allocate(rowTotal.Sum(), weight);
            var allocation = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, random, 10);
            int[][] away = [
                [19, 13, 2, 3, 12, 1, 2, 10, 16, 0],
                [1, 2, 15, 4, 5, 3, 12, 4, 4, 19],
                [0, 0, 1, 0, 1, 0, 1, 0, 0, 1],
                [1, 1, 14, 4, 12, 5, 0, 4, 0, 0],
                [11, 8, 1, 2, 3, 0, 5, 6, 11, 7],
            ];
            if (!TotalsCorrectly(rowTotal, colTotal, away))
                return false;
        var awayColCostError = AllocatorMany.ColCostError(AllocatorMany.ShiftToStartAtZeroAndScaleUp(rowPrice), rowTotal, colTotal, away);
        var awayResult = new AllocatorMany.Result(
            away,
            awayColCostError,
            AllocatorMany.TotalSquaredError(awayColCostError, colTotal),
            false,
            default);
            return allocation.RMSE < awayResult.RMSE;
        }, seed: "0001n4MP9UR1", iter: 1);
    }

    [Test]
    public async Task Example_139x19()
    {
        var fills = new[] {
            (331,1), (350,2), (357,1), (360,3), (366,2), (371,1), (373,1), (375,3), (376,1), (377,2), (378,2), (379,2), (381,2),
            (383,3), (384,2), (385,5), (386,2), (387,1), (389,3), (390,2), (391,1), (392,6), (393,3), (394,3), (395,10), (396,7),
            (397,6), (398,2), (399,5), (400,3), (401,1), (403,4), (404,4), (405,3), (406,2), (407,3), (408,5), (409,3), (410,2),
            (411,1), (415,1), (419,7), (421,1), (424,2), (425,2), (426,2), (436,3), (437,2), (438,1), (446,1), (447,2)
        };
        await Assert.That(fills.Sum(i => i.Item2)).IsEqualTo(139);
        await Assert.That(Check.Equal(fills.Select(i => i.Item1).Distinct(), fills.Select(i => i.Item1))).IsTrue();
        var accounts = new int[] { 2, 2, 2, 2, 2, 3, 5, 5, 6, 7, 11, 12, 13, 17, 50 };
        await Assert.That(accounts.Sum()).IsEqualTo(139);
        var p = Array.ConvertAll(fills, i => i.Item1);
        var q = Array.ConvertAll(fills, i => i.Item2);
        var actual = AllocatorMany.Allocate(p, q, accounts, new Random(123), 10);
    }

    [Test][Skip("remove?")]
    public async Task AllocateTest()
    {
        Gen.Int[2, 20].SelectMany(rows =>
            Gen.Select(
                Gen.Int[1, 300].Array[rows],
                Gen.Int[1, 100].Array[rows],
                Gen.Int[1, 1000].Select(i => (double)i).Array[2, 10],
                Gen.Int.Uniform))
        .Sample((rowPrice, rowTotal, weight, seed) =>
        {
            var random = new Random(seed);
            var colTotal = Allocator.Allocate(rowTotal.Sum(), weight);
            var allocation1 = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, random, 60);
            var allocation2 = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, random, 60);
            return allocation1.RMSE == allocation2.RMSE;
        }, seed: "0001n4MP9UR1", iter: 1);
    }

    private static bool TotalsCorrectly(int[] rowTotal, int[] colTotal, int[][] results)
    {
        for (int i = 0; i < rowTotal.Length; i++)
            if (results[i].Sum() != rowTotal[i])
                return false;
        for (int j = 0; j < colTotal.Length; j++)
            if (results.SumCol(j) != colTotal[j])
                return false;
        return true;
    }

    [Test, Skip("Long-running; run explicitly")]
    public void CertificateNeverCertifiesNonOptimum()
    {
        var iter = EnvInt("SOUND_ITER", 20000);
        int checkedN = 0, withCert = 0;
        Gen.Select(Gen.Int[2, 5], Gen.Int[2, 4]).SelectMany((rows, cols) =>
            Gen.Select(Gen.Int[0, 3].Array[cols].Where(a => a.Sum() > 0).Array[rows], Gen.Int[1, 30].Array[rows]))
        .Sample((solution, rowPrice) =>
        {
            var colTotal = Enumerable.Range(0, solution[0].Length).Select(c => solution.Sum(r => r[c])).ToArray();
            if (!InDomain(rowPrice, colTotal)) return true;
            var rowTotal = Array.ConvertAll(solution, row => row.Sum());
            var b = BruteForce(AllocatorMany.ShiftToStartAtZeroAndScaleUp(rowPrice), rowTotal, colTotal);
            if (b.TooBig) return true;
            checkedN++;
            if (b.AnyCert) withCert++;
            return b.CertSound;
        }, iter: iter, threads: 1);
        TUnitX.WriteLine($"checked={checkedN} withCertifiedOptimum={withCert}");
    }

    [Test, Skip("Long-running; run explicitly")]
    public void EveryCombinationFindsAndProvesOptimum()
    {
        var iter = EnvInt("EC_ITER", 40000);
        int checkedN = 0, provedGlobal = 0;
        // Small instances so the exhaustive search always completes within budget (deterministic result).
        Gen.Select(Gen.Int[2, 4], Gen.Int[2, 3]).SelectMany((rows, cols) =>
            Gen.Select(Gen.Int[0, 2].Array[cols].Where(a => a.Sum() > 0).Array[rows], Gen.Int[1, 20].Array[rows]))
        .Sample((solution, rowPrice) =>
        {
            var colTotal = Enumerable.Range(0, solution[0].Length).Select(c => solution.Sum(r => r[c])).ToArray();
            if (!InDomain(rowPrice, colTotal)) return true;
            var rowTotal = Array.ConvertAll(solution, row => row.Sum());
            var shifted = AllocatorMany.ShiftToStartAtZeroAndScaleUp(rowPrice);
            var b = BruteForce(shifted, rowTotal, colTotal);
            if (b.TooBig) return true;
            checkedN++;
            var results = AllocatorMany.RoundingSolution(rowTotal, colTotal);
            var colError = AllocatorMany.ColCostError(shifted, rowTotal, colTotal, results);
            var seedTse = AllocatorMany.TotalSquaredError(colError, colTotal);
            var inc = new AllocatorMany.Result(results, colError, seedTse, false, AllocatorMany.SolutionType.RoundingMinimum);
            AllocatorMany.EveryCombination(shifted, rowTotal, colTotal, long.MaxValue, ref inc, CancellationToken.None);   // no budget: run to completion
            if (inc.KnownGlobal) provedGlobal++;
            var tol = 1e-9 * (1 + b.MinTSE);
            if (inc.TotalSquaredError < b.MinTSE - tol) return false;                     // never below the true optimum
            return !inc.KnownGlobal || Math.Abs(inc.TotalSquaredError - b.MinTSE) <= tol; // a global claim must be the optimum
        }, iter: iter, threads: 1);
        TUnitX.WriteLine($"checked={checkedN} provedGlobal={provedGlobal}");
    }

    [Test, Skip("Long-running; run explicitly")]
    public void AllocateGlobalClaimsAreCorrect()
    {
        var iter = EnvInt("E2E_ITER", 4000);
        var threads = EnvInt("E2E_THREADS", -1);
        var time = EnvInt("E2E_TIME", 1);
        int checkedN = 0, proved = 0, wrong = 0;
        // Allocate is multithreaded (nondeterministic), so accumulate violations rather than let CsCheck shrink.
        Gen.Select(Gen.Int[2, 5], Gen.Int[2, 4]).SelectMany((rows, cols) =>
            Gen.Select(Gen.Int[0, 3].Array[cols].Where(a => a.Sum() > 0).Array[rows], Gen.Int[1, 30].Array[rows], Gen.Int.Uniform))
        .Sample((solution, rowPrice, seed) =>
        {
            var colTotal = Enumerable.Range(0, solution[0].Length).Select(c => solution.Sum(r => r[c])).ToArray();
            if (!InDomain(rowPrice, colTotal)) return true;
            var rowTotal = Array.ConvertAll(solution, row => row.Sum());
            var b = BruteForce(AllocatorMany.ShiftToStartAtZeroAndScaleUp(rowPrice), rowTotal, colTotal);
            if (b.TooBig) return true;
            checkedN++;
            var res = AllocatorMany.Allocate((int[])rowPrice.Clone(), (int[])rowTotal.Clone(), (int[])colTotal.Clone(), new Random(seed), time, threads);
            var tol = 1e-9 * (1 + b.MinTSE);
            if (!TotalsCorrectly(rowTotal, colTotal, res.Solution)) wrong++;                    // must be feasible
            if (res.TotalSquaredError < b.MinTSE - tol) wrong++;                                // never below the optimum
            if (res.KnownGlobal) { proved++; if (Math.Abs(res.TotalSquaredError - b.MinTSE) > tol) wrong++; }  // a global claim must be the optimum
            return true;
        }, iter: iter, threads: 1);
        TUnitX.WriteLine($"checked={checkedN} provedGlobal={proved} wrong={wrong}");
        if (wrong > 0) throw new Exception($"Allocate produced {wrong} infeasible/below-optimum/false-global results");
    }

    private static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : dflt;

    private static long DivRound(long x, long y) => (y / 2 + x) / y;

    private readonly record struct BruteResult(double MinTSE, bool CertSound, bool AnyCert, bool TooBig);

    // Enumerate every feasible matrix with the given margins: the true minimum TSE, and a check that the
    // certificate (IsRelaxedOptimal) never declares a non-optimal solution global. TooBig if past the node cap.
    private static BruteResult BruteForce(int[] priceShifted, int[] rowTotal, int[] colTotal)
    {
        int rows = rowTotal.Length, cols = colTotal.Length;
        var tq = 0; var totalCost = 0L;
        for (int i = 0; i < rows; i++) { tq += rowTotal[i]; totalCost += (long)priceShifted[i] * rowTotal[i]; }
        var target = new long[cols];
        for (int j = 0; j < cols; j++) target[j] = DivRound((long)colTotal[j] * totalCost, tq);
        var x = new int[rows][];
        for (int i = 0; i < rows; i++) x[i] = new int[cols];
        var colRem = (int[])colTotal.Clone();
        var colErr = new int[cols];
        var best = double.PositiveInfinity;
        var certMax = double.NegativeInfinity;
        var anyCert = false;
        var nodes = 0L;
        const long cap = 5_000_000;
        var overflow = false;

        void Eval()
        {
            var tse = 0.0;
            for (int j = 0; j < cols; j++)
            {
                var cost = 0L;
                for (int i = 0; i < rows; i++) cost += (long)priceShifted[i] * x[i][j];
                colErr[j] = (int)(cost - target[j]);
                var e = colErr[j] / (double)colTotal[j];
                tse += e * e;
            }
            if (tse < best) best = tse;
            if (AllocatorMany.IsRelaxedOptimal(colErr, colTotal)) { anyCert = true; if (tse > certMax) certMax = tse; }
        }
        void Rec(int i, int j, int rowRem)
        {
            if (overflow) return;
            if (++nodes > cap) { overflow = true; return; }
            if (j == cols - 1)
            {
                if (rowRem <= colRem[j])
                {
                    x[i][j] = rowRem; colRem[j] -= rowRem;
                    if (i == rows - 1) { var ok = true; for (int c = 0; c < cols; c++) if (colRem[c] != 0) { ok = false; break; } if (ok) Eval(); }
                    else Rec(i + 1, 0, rowTotal[i + 1]);
                    colRem[j] += rowRem;
                }
                return;
            }
            var hi = Math.Min(rowRem, colRem[j]);
            for (int v = 0; v <= hi; v++)
            {
                x[i][j] = v; colRem[j] -= v;
                Rec(i, j + 1, rowRem - v);
                colRem[j] += v;
            }
        }
        Rec(0, 0, rowTotal[0]);
        if (overflow) return new BruteResult(double.NaN, true, false, true);
        var certSound = !anyCert || certMax <= best + 1e-9 * (1 + best);
        return new BruteResult(best, certSound, anyCert, false);
    }

    // AllocateCore's preconditions for the bound: no zero columns, not all col totals <= 1, >= 2 distinct prices.
    private static bool InDomain(int[] rowPrice, int[] colTotal)
        => !Array.Exists(colTotal, c => c == 0) && colTotal.Max() > 1 && rowPrice.Distinct().Skip(1).Any();
}
