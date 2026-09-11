namespace Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public static class AllocatorMany
{
    private const int _pricePrecisionFactor = 100;

    public enum SolutionType
    {
        SingleRow,
        SingleGroupRow,
        SingleColumn,
        OnesColumn,
        RoundingMinimum,
        RandomChange,
        EveryCombination,
    }

    public record Result(int[][] Solution, int[] ColumnCostError, double TotalSquaredError, bool KnownGlobal, SolutionType SolutionType)
    {
        public double RMSE => Math.Sqrt(TotalSquaredError / ColumnCostError.Length) / _pricePrecisionFactor;
    }

    // Proven-global test. A feasible incumbent's column errors satisfy Σ_j d_j = δ (error_j = PF*d_j - r_j,
    // fixed because Σ cost_j = totalCost). Minimising Σ (error_j/C_j)² under only that identity is a lower
    // bound, and the incumbent attains it (so is global) iff no single unit of deviation moved between two
    // columns helps: min_j (2*e_j + PF)/C_j² >= max_i (2*e_i - PF)/C_i². When lo/hi box bounds are provided
    // (achievable deviation range per column from ColBox), only feasible transfers are considered, a tighter bound.
    // Compared exactly with Int128 (wide enough that C_j²*error can't overflow). Requires post-shift errors.
    internal static bool IsRelaxedOptimal(int[] columnCostError, int[] colTotal, int[]? lo = null, int[]? hi = null)
    {
        const long PF = _pricePrecisionFactor;
        long maxRemNum = 0, maxRemDen = 1, minAddNum = 0, minAddDen = 1;
        bool hasRem = false, hasAdd = false;
        for (int j = 0; j < columnCostError.Length; j++)
        {
            long e = columnCostError[j];
            var den = (long)colTotal[j] * colTotal[j];
            var rem = 2 * e - PF;
            var add = 2 * e + PF;
            bool canRemove = true, canAdd = true;
            if (lo is not null || hi is not null)
            {
                // d_j = (e + r_j) / PF where r_j ≡ -e (mod PF), adjusted to (-PF/2, PF/2]
                long r = ((-e % PF) + PF) % PF;
                if (r > PF / 2) r -= PF;
                long d = (e + r) / PF;
                if (lo != null && d <= lo[j]) canRemove = false;
                if (hi != null && d >= hi[j]) canAdd = false;
            }
            if (canRemove && (!hasRem || (Int128)rem * maxRemDen > (Int128)maxRemNum * den)) { maxRemNum = rem; maxRemDen = den; hasRem = true; }
            if (canAdd   && (!hasAdd  || (Int128)add * minAddDen < (Int128)minAddNum * den)) { minAddNum = add; minAddDen = den; hasAdd  = true; }
        }
        return !hasRem || !hasAdd || (Int128)minAddNum * maxRemDen >= (Int128)maxRemNum * minAddDen;
    }

    // Per-column achievable deviation range [lo[j], hi[j]] on d_j = (cost_j - m_j) / PF, computed by
    // greedily filling colTotal[j] units with the cheapest/dearest rows (ignoring cross-column contention).
    // Prices after ShiftToStartAtZeroAndScaleUp are multiples of PF, so the division is exact.
    private static (int[] lo, int[] hi) ColBox(int[] rowPrice, int[] rowTotal, int[] colTotal, long totalCost, int totalQuantity)
    {
        const long PF = _pricePrecisionFactor;
        int rows = rowPrice.Length, cols = colTotal.Length;
        var order = new int[rows];
        for (int i = 0; i < rows; i++) order[i] = i;
        Array.Sort(order, (a, b) => rowPrice[a].CompareTo(rowPrice[b]));
        var lo = new int[cols];
        var hi = new int[cols];
        for (int j = 0; j < cols; j++)
        {
            var target = DivRound((long)colTotal[j] * totalCost, totalQuantity);
            var m = DivRound(target, PF) * PF;
            long minCost = 0; int remMin = colTotal[j];
            for (int k = 0; k < rows && remMin > 0; k++) { var t = Math.Min(remMin, rowTotal[order[k]]); minCost += (long)rowPrice[order[k]] * t; remMin -= t; }
            long maxCost = 0; int remMax = colTotal[j];
            for (int k = rows - 1; k >= 0 && remMax > 0; k--) { var t = Math.Min(remMax, rowTotal[order[k]]); maxCost += (long)rowPrice[order[k]] * t; remMax -= t; }
            lo[j] = (int)((minCost - m) / PF);
            hi[j] = (int)((maxCost - m) / PF);
        }
        return (lo, hi);
    }

    public static Result Allocate(int[] rowPrice, int[] rowTotal, int[] colTotal, Random random, int time = 10, int threads = -1)
    {
        if (rowPrice.Length != rowTotal.Length)
            throw new Exception($"rowPrice must be the same length as rowLength {rowPrice.Length}!={rowTotal.Length}");
        if (rowTotal.Sum() != colTotal.Sum())
            throw new Exception($"rowTotal.Sum()!=colTotal.Sum() {rowTotal.Sum()}!={colTotal.Sum()}");

        if (rowPrice.Length == 1)
            return new Result([(int[])colTotal.Clone()], new int[colTotal.Length], 0, true, SolutionType.SingleRow);

        if (colTotal.Count(i => i != 0) == 1) // One columnTotal non zero return simple solution
        {
            var j = Array.FindIndex(colTotal, i => i != 0);
            var solution = new int[rowTotal.Length][];
            for (int i = 0; i < solution.Length; i++)
            {
                var row = new int[colTotal.Length];
                row[j] = rowTotal[i];
                solution[i] = row;
            }
            return new Result(solution, new int[colTotal.Length], 0, true, SolutionType.SingleColumn);
        }

        bool negative = false;
        if (rowTotal.Any(i => i < 0) || colTotal.Any(i => i < 0)) // Check sign and negate if necessary
        {
            if (colTotal.Any(i => i > 0) || rowTotal.Any(i => i > 0))
                throw new Exception($"rowTotal and colTotal must all be same sign rowTotal={string.Join(',', rowTotal)} colTotal={string.Join(',', colTotal)}");
            negative = true;
            rowTotal = Array.ConvertAll(rowTotal, i => -i);
            colTotal = Array.ConvertAll(colTotal, i => -i);
        }

        Result result;
        HashSet<int>? zeroColumns = null;
        Dictionary<int, int>? rowGroup = null;

        if (colTotal.All(i => i <= 1)) // All columnTotal <= 1 return simple solution
        {
            var j = 0;
            var solution = new int[rowTotal.Length][];
            for (int i = 0; i < solution.Length; i++)
            {
                var row = new int[colTotal.Length];
                var q = rowTotal[i];
                while (q-- > 0)
                {
                    while (colTotal[j] == 0)
                        j++;
                    row[j++] = 1;
                }
                solution[i] = row;
            }
            var colError = ColCostError(rowPrice, rowTotal, colTotal, solution);
            var minError = TotalSquaredError(colError, colTotal);
            result = new Result(solution, colError, minError, true, SolutionType.OnesColumn);
        }
        else
        {
            // Deduplicate by price
            rowGroup = GroupByPrice(rowPrice, rowTotal);

            // One row return
            if (rowGroup?.Count == 1)
            {
                result = new Result([(int[])colTotal.Clone()], new int[colTotal.Length], 0, true, SolutionType.SingleGroupRow);
            }
            else
            {
                // Remove zero column total
                var zeroColCount = colTotal.Count(i => i == 0);
                if (zeroColCount > 0)
                {
                    zeroColumns = [];
                    var nonZero = new int[colTotal.Length - zeroColCount];
                    int inz = 0;
                    for (int i = 0; i < colTotal.Length; i++)
                    {
                        var ct = colTotal[i];
                        if (ct == 0)
                            zeroColumns.Add(i);
                        else
                            nonZero[inz++] = ct;
                    }
                    colTotal = nonZero;
                }
                var (rowPriceGroup, rowTotalGroup) = rowGroup is null ? (rowPrice, rowTotal)
                                                   : (rowGroup.Keys.ToArray(), rowGroup.Values.ToArray());
                result = AllocateCore(rowPriceGroup, rowTotalGroup, colTotal, random, time, threads);
            }
        }

        if (rowGroup is not null)
        {
            var solution = UnGroupByPrice(rowPrice, rowTotal, rowGroup, result.Solution);
            result = result with { Solution = solution };
        }

        if (zeroColumns is not null)
        {
            var solution = result.Solution;
            for (var i = 0; i < solution.Length; i++)
            {
                var row = solution[i];
                var newRow = new int[row.Length + zeroColumns.Count];
                var jOld = 0;
                for (var j = 0; j < newRow.Length; j++)
                {
                    if (!zeroColumns.Contains(j))
                        newRow[j] = row[jOld++];
                }
                solution[i] = newRow;
            }
        }

        if (negative)
        {
            var solution = result.Solution;
            for (var i = 0; i < solution.Length; i++)
            {
                var row = solution[i];
                for (var j = 0; j < row.Length; j++)
                    row[j] = -row[j];
            }
        }

        return result;
    }

    private static Result AllocateCore(int[] rowPrice, int[] rowTotal, int[] colTotal, Random random, int time, int threads)
    {
        rowPrice = ShiftToStartAtZeroAndScaleUp(rowPrice);
        var minimum = RoundingSolutionThenFindLocalMinimum(rowPrice, rowTotal, colTotal);
        if (minimum.KnownGlobal)
            return minimum;

        if (threads <= 0) threads = Environment.ProcessorCount;

        // The time budget is a deadline checked inline by the searching threads, robust when the thread pool is
        // oversubscribed, unlike a CancelAfter timer whose callback can be starved for many seconds.
        var finishTime = Stopwatch.GetTimestamp() + Math.Max(0, time) * Stopwatch.Frequency;

        var (tq, tc) = TotalQuantityAndCost(rowPrice, rowTotal);
        var (lo, hi) = ColBox(rowPrice, rowTotal, colTotal, tc, tq);

        if (threads == 1)
        {
            var results = Copy(minimum.Solution);
            var colError = (int[])minimum.ColumnCostError.Clone();
            var random2 = new Random(random.Next());
            while (!minimum.KnownGlobal && Stopwatch.GetTimestamp() < finishTime)
            {
                // ILS: perturb a copy of the best; discard on failure so we always restart from the best.
                var work = Copy(results);
                var workErr = (int[])colError.Clone();
                RandomChange(rowPrice, work, workErr, random2);
                FindLocalMinimum(rowPrice, colTotal, work, workErr);
                var totalError = TotalSquaredError(workErr, colTotal);
                if (totalError < minimum.TotalSquaredError)
                {
                    minimum = new Result(work, workErr, totalError, IsRelaxedOptimal(workErr, colTotal, lo, hi), SolutionType.RandomChange);
                    results = work;
                    colError = workErr;
                }
            }
            return minimum;
        }

        // The token only signals "stop": a worker or the exhaustive search proved global, or time<=0 (no search).
        using var cts = new CancellationTokenSource();
        if (time <= 0) cts.Cancel();
        var token = cts.Token;

        // ILS workers run alongside the exhaustive search on this thread, sharing no mutable state:
        // each worker owns its buffers and publishes immutable Results under `gate`, and the search uses a
        // private incumbent so its "completed => global" can't be corrupted. Workers are joined, so nothing
        // keeps mutating state after we return.
        var gate = new Lock();
        var best = minimum;

        var tasks = new Task[threads - 1];
        for (int t = 0; t < tasks.Length; t++)
        {
            var rnd = new Random(random.Next());   // seed here: Random isn't thread-safe and `random` is shared
            tasks[t] = Task.Run(() =>
            {
                var results = Copy(minimum.Solution);
                var colError = (int[])minimum.ColumnCostError.Clone();
                var workerBestTse = minimum.TotalSquaredError;
                while (!token.IsCancellationRequested && Stopwatch.GetTimestamp() < finishTime)
                {
                    // ILS: perturb a copy of this worker's best; discard on failure so we always restart from the best.
                    var work = Copy(results);
                    var workErr = (int[])colError.Clone();
                    RandomChange(rowPrice, work, workErr, rnd);
                    FindLocalMinimum(rowPrice, colTotal, work, workErr);
                    var totalError = TotalSquaredError(workErr, colTotal);
                    if (totalError < workerBestTse)
                    {
                        workerBestTse = totalError;
                        results = work;
                        colError = workErr;
                        if (totalError < best.TotalSquaredError)   // best only decreases, so a stale read is safe here
                            lock (gate)
                                if (totalError < best.TotalSquaredError)
                                {
                                    var isGlobal = IsRelaxedOptimal(workErr, colTotal, lo, hi);
                                    best = new Result(work, workErr, totalError, isGlobal, SolutionType.RandomChange);
                                    if (isGlobal) cts.Cancel();
                                }
                    }
                }
            });
        }

        // Safe to share minimum's arrays: EveryCombination only replaces its incumbent, never mutates it in place.
        var everyResult = minimum;
        EveryCombination(rowPrice, rowTotal, colTotal, finishTime, ref everyResult, token);
        cts.Cancel();
        Task.WaitAll(tasks);   // establishes happens-before, so `best` is fully visible without the lock

        var winner = everyResult.TotalSquaredError <= best.TotalSquaredError ? everyResult : best;
        if (!winner.KnownGlobal && IsRelaxedOptimal(winner.ColumnCostError, colTotal, lo, hi))
            winner = winner with { KnownGlobal = true };
        return winner;
    }

    private static void RandomChange(int[] rowPrice, int[][] results, int[] colError, Random random)
    {
        var triesLimit = random.Next(2, 10);
        var count = 0;
        var tries = 0;
        while (count < 2 || tries < triesLimit)
        {
            var i1 = random.Next(0, rowPrice.Length - 1);
            var i2 = random.Next(i1 + 1, rowPrice.Length);
            var j1 = random.Next(0, colError.Length - 1);
            var j2 = random.Next(j1 + 1, colError.Length);
            var change = random.Next(0, 2) * 2 - 1;
            var colChange = (rowPrice[i1] - rowPrice[i2]) * change;
            tries++;
            var results_i1 = results[i1];
            var results_i2 = results[i2];
            if (results_i1[j1] + change >= 0
             && results_i1[j2] >= change
             && results_i2[j2] + change >= 0
             && results_i2[j1] >= change)
            {
                results_i1[j1] += change;
                results_i1[j2] -= change;
                results_i2[j2] += change;
                results_i2[j1] -= change;
                colError[j1] += colChange;
                colError[j2] -= colChange;
                count++;
            }
        }
    }

    private static double Sqr(double x) => x * x;

    private static int[][] Copy(this int[][] a)
    {
        var r = new int[a.Length][];
        for (int i = 0; i < a.Length; i++)
            r[i] = (int[])a[i].Clone();
        return r;
    }

    private static (int, long) TotalQuantityAndCost(int[] rowPrice, int[] rowTotal)
    {
        var totalQuantity = 0;
        var totalCost = 0L;
        for (int i = 0; i < rowTotal.Length; i++)
        {
            var q = rowTotal[i];
            totalQuantity += q;
            totalCost += (long)rowPrice[i] * q;
        }
        return (totalQuantity, totalCost);
    }

    // col cost - target cost
    internal static int[] ColCostError(int[] rowPrice, int[] rowTotal, int[] colTotal, int[][] results)
    {
        var (totalQuantity, totalCost) = TotalQuantityAndCost(rowPrice, rowTotal);
        var colError = new int[colTotal.Length];
        for (int j = 0; j < colTotal.Length; j++)
        {
            var cost = 0L;
            for (int i = 0; i < results.Length; i++)
                cost += (long)rowPrice[i] * results[i][j];
            colError[j] = (int)(cost - DivRound(colTotal[j] * totalCost, totalQuantity));
        }
        return colError;
    }

    internal static double TotalSquaredError(int[] colCostError, int[] colTotal)
    {
        var error = 0.0;
        for (int i = 0; i < colCostError.Length; i++)
        {
            var colTotal_i = colTotal[i];
            if (colTotal_i != 0)
                error += Sqr((double)colCostError[i] / colTotal_i);
        }
        return error;
    }

    private static void FindLocalMinimum(int[] rowPrice, int[] colTotal, int[][] results, int[] colError)
    {
        while (true)
        {
            var reductionBest = 0.0;
            int changeBest = 0, colChangeBest = 0, i1Best = 0, i2Best = 0, j1Best = 0, j2Best = 0;
            for (int i1 = 0; i1 < rowPrice.Length - 1; i1++)
            {
                var resultsi1 = results[i1];
                var pricei1 = rowPrice[i1];
                for (int j1 = 0; j1 < colError.Length - 1; j1++)
                {
                    var colTotalj1 = colTotal[j1];
                    var colErrorj1 = colError[j1];
                    for (int i2 = i1 + 1; i2 < rowPrice.Length; i2++)
                    {
                        var resultsi2 = results[i2];
                        var colChange = pricei1 - rowPrice[i2];
                        for (int j2 = j1 + 1; j2 < colError.Length; j2++)
                        {
                            var colTotalj2 = colTotal[j2];
                            var colErrorj2 = colError[j2];
                            var reduction = Sqr((double)colErrorj1 / colTotalj1) - Sqr((double)(colErrorj1 + colChange) / colTotalj1)
                                          + Sqr((double)colErrorj2 / colTotalj2) - Sqr((double)(colErrorj2 - colChange) / colTotalj2);
                            if (reduction > reductionBest && resultsi1[j2] != 0 && resultsi2[j1] != 0)
                            {
                                reductionBest = reduction;
                                changeBest = 1;
                                colChangeBest = colChange;
                                i1Best = i1; i2Best = i2; j1Best = j1; j2Best = j2;
                            }
                            reduction = Sqr((double)colErrorj1 / colTotalj1) - Sqr((double)(colErrorj1 - colChange) / colTotalj1)
                                      + Sqr((double)colErrorj2 / colTotalj2) - Sqr((double)(colErrorj2 + colChange) / colTotalj2);
                            if (reduction > reductionBest && resultsi1[j1] != 0 && resultsi2[j2] != 0)
                            {
                                reductionBest = reduction;
                                changeBest = -1;
                                colChangeBest = -colChange;
                                i1Best = i1; i2Best = i2; j1Best = j1; j2Best = j2;
                            }
                        }
                    }
                }
            }
            if (reductionBest == 0) return;
            var row = results[i1Best];
            row[j1Best] += changeBest;
            row[j2Best] -= changeBest;
            row = results[i2Best];
            row[j2Best] += changeBest;
            row[j1Best] -= changeBest;
            colError[j1Best] += colChangeBest;
            colError[j2Best] -= colChangeBest;
        }
    }

    // Exhaustive branch-and-bound over every feasible matrix: fill columns left to right, each non-final column
    // enumerating all row distributions of its total, the last column forced by row remainders; prune when the
    // completed columns' error alone reaches the incumbent. If it finishes uninterrupted the incumbent is the
    // true global optimum. Single-threaded on a private incumbent (by ref); improved leaves also try IsRelaxedOptimal.
    internal static void EveryCombination(int[] rowPrice, int[] rowTotal, int[] colTotal, long finishTime, ref Result minimum, CancellationToken token)
    {
        var (totalQuantity, totalCost) = TotalQuantityAndCost(rowPrice, rowTotal);
        var (lo, hi) = ColBox(rowPrice, rowTotal, colTotal, totalCost, totalQuantity);
        var rows = rowTotal.Length;
        var cols = colTotal.Length;
        var x = new int[rows][];
        for (int i = 0; i < rows; i++) x[i] = new int[cols];
        var rowRem = (int[])rowTotal.Clone();
        var target = new long[cols];
        for (int j = 0; j < cols; j++) target[j] = DivRound((long)colTotal[j] * totalCost, totalQuantity);
        var cancelled = false;
        var tick = 0L;
        var incumbent = minimum;

        double ColErrorSq(int col)
        {
            var cost = 0L;
            for (int i = 0; i < rows; i++) cost += (long)rowPrice[i] * x[i][col];
            var e = (double)(cost - target[col]) / colTotal[col];
            return e * e;
        }
        void Publish(double err)
        {
            var sol = Copy(x);
            var ce = new int[cols];
            for (int j = 0; j < cols; j++)
            {
                var cost = 0L;
                for (int i = 0; i < rows; i++) cost += (long)rowPrice[i] * x[i][j];
                ce[j] = (int)(cost - target[j]);
            }
            incumbent = new Result(sol, ce, err, IsRelaxedOptimal(ce, colTotal, lo, hi), SolutionType.EveryCombination);
        }
        // The deadline or another thread's proof latches into `cancelled` so the per-branch fast-abort can unwind
        // the recursion with a cheap local read. The clock is polled only every 8192 calls (it dominates otherwise).
        bool Interrupted()
        {
            if (cancelled || incumbent.KnownGlobal) return true;
            if (token.IsCancellationRequested || ((++tick & 8191) == 0 && Stopwatch.GetTimestamp() >= finishTime))
                cancelled = true;
            return cancelled;
        }
        void EnumCol(int col, int row, int remaining, double errSoFar)
        {
            if (cancelled || incumbent.KnownGlobal) return;  // cheap fast-abort, reached on every branch
            if (col == cols - 1)                             // last column is forced by the row remainders
            {
                if (Interrupted()) return;
                for (int i = 0; i < rows; i++) x[i][col] = rowRem[i];
                var err = errSoFar + ColErrorSq(col);
                if (err <= incumbent.TotalSquaredError) Publish(err);
                return;
            }
            if (row == rows - 1)
            {
                if (Interrupted()) return;                   // budget check, reached even when pruned
                if (remaining <= rowRem[row])
                {
                    x[row][col] = remaining; rowRem[row] -= remaining;
                    var err = errSoFar + ColErrorSq(col);
                    if (err < incumbent.TotalSquaredError) EnumCol(col + 1, 0, colTotal[col + 1], err);
                    rowRem[row] += remaining; x[row][col] = 0;
                }
                return;
            }
            var hi = Math.Min(remaining, rowRem[row]);
            for (int v = 0; v <= hi; v++)
            {
                if (cancelled || incumbent.KnownGlobal) return;
                x[row][col] = v; rowRem[row] -= v;
                EnumCol(col, row + 1, remaining - v, errSoFar);
                rowRem[row] += v; x[row][col] = 0;
            }
        }

        EnumCol(0, 0, colTotal[0], 0.0);
        if (!cancelled && !incumbent.KnownGlobal)   // finished the whole tree uninterrupted => optimum proven
            incumbent = incumbent with { KnownGlobal = true };
        minimum = incumbent;
    }

    private static Result RoundingSolutionThenFindLocalMinimum(int[] rowPrice, int[] rowTotal, int[] colTotal)
    {
        var results = RoundingSolution(rowTotal, colTotal);
        var colError = ColCostError(rowPrice, rowTotal, colTotal, results);
        FindLocalMinimum(rowPrice, colTotal, results, colError);
        var minError = TotalSquaredError(colError, colTotal);
        var (tq, tc) = TotalQuantityAndCost(rowPrice, rowTotal);
        var (lo, hi) = ColBox(rowPrice, rowTotal, colTotal, tc, tq);
        return new Result(results, colError, minError, IsRelaxedOptimal(colError, colTotal, lo, hi), SolutionType.RoundingMinimum);
    }

    internal static int[][] RoundingSolution(int[] rowTotal, int[] colTotal)
    {
        var total = colTotal.Sum();
        var results = new int[rowTotal.Length][];
        for (int i = 0; i < results.Length; i++)
        {
            var q = rowTotal[i];

            var row = new int[colTotal.Length];
            for (int j = 0; j < row.Length; j++)
                row[j] = (int)Math.Round(((double)(q * colTotal[j])) / total);
            results[i] = row;
        }

        // 1. Correct any results where there is a matching rowTotal and colTotal direction needed.
        while (true)
        {
            var costBest = double.MaxValue;
            int iBest = 0, jBest = 0, changeBest = 0;
            for (int j = 0; j < colTotal.Length; j++)
            {
                var colNeeded = colTotal[j] - results.SumCol(j);
                if (colNeeded != 0)
                    for (int i = 0; i < results.Length; i++)
                    {
                        var rowNeeded = rowTotal[i] - results[i].Sum();
                        if (rowNeeded != 0)
                        {
                            var change = colNeeded > 0 && rowNeeded > 0 ? 1
                                       : colNeeded < 0 && rowNeeded < 0 ? -1
                                       : 0;
                            if (change != 0)
                            {
                                var target = (double)(rowTotal[i] * colTotal[j]) / total;
                                var cost = Math.Abs(results[i][j] + change - target) - Math.Abs(results[i][j] - target);
                                if (cost < costBest)
                                {
                                    costBest = cost;
                                    changeBest = change;
                                    iBest = i;
                                    jBest = j;
                                }
                            }
                        }
                    }
            }
            if (costBest == double.MaxValue)
                break;
            results[iBest][jBest] += changeBest;
        }

        // 2. Correct any results where there is are offsetting rowTotals needed.
        while (true)
        {
            var costBest = double.MaxValue;
            int iBest1 = 0, iBest2 = 0, jBest = 0, changeBest = 0;
            for (int i1 = 0; i1 < results.Length; i1++)
            {
                var rowNeeded1 = rowTotal[i1] - results[i1].Sum();
                if (rowNeeded1 != 0)
                {
                    for (int i2 = i1 + 1; i2 < results.Length; i2++)
                    {
                        var rowNeeded2 = rowTotal[i2] - results[i2].Sum();
                        if (rowNeeded2 != 0)
                        {
                            var change = rowNeeded1 > 0 && rowNeeded2 < 0 ? 1
                                       : rowNeeded1 < 0 && rowNeeded2 > 0 ? -1
                                       : 0;
                            if (change != 0)
                            {
                                for (int j = 0; j < colTotal.Length; j++)
                                {
                                    var target1 = (double)(rowTotal[i1] * colTotal[j]) / total;
                                    var target2 = (double)(rowTotal[i2] * colTotal[j]) / total;
                                    var cost = Math.Abs(results[i1][j] + change - target1) - Math.Abs(results[i1][j] - target1)
                                             + Math.Abs(results[i2][j] - change - target2) - Math.Abs(results[i2][j] - target2);
                                    if (cost < costBest)
                                    {
                                        costBest = cost;
                                        changeBest = change;
                                        iBest1 = i1;
                                        iBest2 = i2;
                                        jBest = j;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (costBest == double.MaxValue)
                break;
            results[iBest1][jBest] += changeBest;
            results[iBest2][jBest] -= changeBest;
        }

        // 3. Correct any results where there is are offsetting colTotals needed.
        while (true)
        {
            var costBest = double.MaxValue;
            int iBest = 0, jBest1 = 0, jBest2 = 0, changeBest = 0;
            for (int j1 = 0; j1 < colTotal.Length; j1++)
            {
                var colNeeded1 = colTotal[j1] - results.SumCol(j1);
                if (colNeeded1 != 0)
                {
                    for (int j2 = j1 + 1; j2 < colTotal.Length; j2++)
                    {
                        var colNeeded2 = colTotal[j2] - results.SumCol(j2);
                        if (colNeeded2 != 0)
                        {
                            var change = colNeeded1 > 0 && colNeeded2 < 0 ? 1
                                       : colNeeded1 < 0 && colNeeded2 > 0 ? -1
                                       : 0;
                            if (change != 0)
                            {
                                for (int i = 0; i < results.Length; i++)
                                {
                                    var target1 = (double)(rowTotal[i] * colTotal[j1]) / total;
                                    var target2 = (double)(rowTotal[i] * colTotal[j2]) / total;
                                    var cost = Math.Abs(results[i][j1] + change - target1) - Math.Abs(results[i][j1] - target1)
                                             + Math.Abs(results[i][j2] - change - target2) - Math.Abs(results[i][j2] - target2);
                                    if (cost < costBest)
                                    {
                                        costBest = cost;
                                        changeBest = change;
                                        iBest = i;
                                        jBest1 = j1;
                                        jBest2 = j2;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (costBest == double.MaxValue)
                break;
            results[iBest][jBest1] += changeBest;
            results[iBest][jBest2] -= changeBest;
        }
        return results;
    }

    internal static Dictionary<int, int>? GroupByPrice(int[] rowPrice, int[] rowTotal)
    {
        var d = new Dictionary<int, int>(rowPrice.Length);
        for (int i = 0; i < rowPrice.Length; i++)
        {
            var price = rowPrice[i];
            var t = rowTotal[i];
            if (!d.TryAdd(price, t))
                d[price] += t;
        }
        return d.Count == rowPrice.Length ? null : d;
    }

    internal static int[][] UnGroupByPrice(int[] rowPrice, int[] rowTotal, Dictionary<int, int> rowGroup, int[][] solutionGroup)
    {
        var remaining = new Dictionary<int, int[]>();
        var solution = new int[rowPrice.Length][];
        var g = 0;
        for (int i = 0; i < rowPrice.Length; i++)
        {
            var price = rowPrice[i];
            var needed = rowTotal[i];
            if (needed == rowGroup[price])
            {
                solution[i] = solutionGroup[g++];
            }
            else
            {
                if (!remaining.TryGetValue(price, out var remainingRow))
                    remaining.Add(price, remainingRow = solutionGroup[g++]);
                if (remainingRow.Sum() == needed)
                {
                    solution[i] = remainingRow;
                }
                else
                {
                    var row = new int[remainingRow.Length];
                    for (int j = 0; j < remainingRow.Length; j++)
                    {
                        var v = remainingRow[j];
                        if (v >= needed)
                        {
                            row[j] = needed;
                            remainingRow[j] -= needed;
                            break;
                        }
                        else
                        {
                            row[j] = v;
                            remainingRow[j] = 0;
                            needed -= v;
                        }
                    }
                    solution[i] = row;
                }
            }
        }
        return solution;
    }

    private static long DivRound(long x, long y)
    {
        return (y / 2 + x) / y;
    }

    internal static int[] ShiftToStartAtZeroAndScaleUp(int[] price)
    {
        price = (int[])price.Clone();
        var min = price.Min();
        for (int i = 0; i < price.Length; i++)
        {
            price[i] -= min;
            price[i] *= _pricePrecisionFactor;
        }
        return price;
    }

    internal static int SumCol(this int[][] values, int j)
    {
        var sum = 0;
        for (int i = 0; i < values.Length; i++)
            sum += values[i][j];
        return sum;
    }
}