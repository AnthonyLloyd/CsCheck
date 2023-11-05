namespace Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

#nullable enable

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

    private static bool CheckGlobal(int[] columnCostError)
        => !Array.Exists(columnCostError, e => Math.Abs(e) > _pricePrecisionFactor / 2);

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
        var finishTime = Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
        rowPrice = ShiftToStartAtZeroAndScaleUp(rowPrice);
        var minimum = RoundingSolutionThenFindLocalMinimum(rowPrice, rowTotal, colTotal);
        if (minimum.KnownGlobal)
            return minimum;

        Action CreateAction()
        {
            var random2 = new Random(random.Next());
            var results = Copy(minimum.Solution);
            var colError = (int[])minimum.ColumnCostError.Clone();
            return () =>
            {
                RandomChange(rowPrice, results, colError, random2);
                FindLocalMinimum(rowPrice, colTotal, results, colError);
                var totalError = TotalSquaredError(colError, colTotal);
                if (totalError < minimum.TotalSquaredError)
                    lock (colTotal)
                        if (totalError < minimum.TotalSquaredError)
                        {
                            minimum = new Result(results, colError, totalError, CheckGlobal(colError), SolutionType.RandomChange);
                            results = Copy(results);
                            colError = (int[])colError.Clone();
                        }
            };
        }
        if (threads <= 0) threads = Environment.ProcessorCount;
        if (threads == 1)
        {
            var action = CreateAction();
            while (!minimum.KnownGlobal && finishTime > Stopwatch.GetTimestamp())
                action();
        }
        else
        {
            while (--threads > 0)
            {
                var threadAction = CreateAction();
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    while (!minimum.KnownGlobal && finishTime > Stopwatch.GetTimestamp())
                        threadAction();
                }, null);
            }
            EveryCombination(rowPrice, rowTotal, colTotal, finishTime, ref minimum);
        }
        return minimum;
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
             && results_i1[j2] - change >= 0
             && results_i2[j2] + change >= 0
             && results_i2[j1] - change >= 0)
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

    private static void CopyTo(this int[][] a, int[][] r)
    {
        for (int i = 0; i < a.Length; i++)
            a[i].CopyTo(r[i], 0);
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

    internal static int ColCostError(int[] rowPrice, int[] colTotal, int[][] results, long totalCost, int totalQuantity, int col)
    {
        var cost = 0L;
        for (int i = 0; i < results.Length; i++)
            cost += (long)rowPrice[i] * results[i][col];
        return (int)(cost - DivRound(colTotal[col] * totalCost, totalQuantity));
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

    private static int SumStart(this int[] row, int j)
    {
        var r = 0;
        for (int i = j; i < row.Length; i++)
            r += row[i];
        return r;
    }

    public static void EveryCombination(int[] rowPrice, int[] rowTotal, int[] colTotal, long finishTime, ref Result minimum)
    {
        var (totalQuantity, totalCost) = TotalQuantityAndCost(rowPrice, rowTotal);
        var completed = EveryCombination(rowPrice, rowTotal, colTotal, totalQuantity, totalCost,
            Copy(minimum.Solution), colTotal.Length - 1, new int[colTotal.Length], 0.0, finishTime, ref minimum);
        if (completed && !minimum.KnownGlobal)
            lock (colTotal)
                if (!minimum.KnownGlobal)
                    minimum = minimum with { KnownGlobal = true };
    }

    public static bool EveryCombination(int[] rowPrice, int[] rowTotal, int[] colTotal, int totalQuantity, long totalCost,
        int[][] solution, int col, int[] colErrorSoFar, double totalErrorSoFar, long finishTime, ref Result minimum)
    {
        if (col == 0)
        {
            for (int i = 0; i < rowTotal.Length; i++)
            {
                var row = solution[i];
                row[0] = rowTotal[i] - row.SumStart(1);
            }
            // calculate errors
            var colError_0 = ColCostError(rowPrice, colTotal, solution, totalCost, totalQuantity, 0);
            totalErrorSoFar += Sqr((double)colError_0 / colTotal[0]);
            if (totalErrorSoFar <= minimum.TotalSquaredError)
                lock (colTotal)
                    if (totalErrorSoFar <= minimum.TotalSquaredError)
                    {
                        colErrorSoFar[0] = colError_0;
                        colErrorSoFar.CopyTo(minimum.ColumnCostError, 0);
                        solution.CopyTo(minimum.Solution);
                        minimum = new Result(minimum.Solution, minimum.ColumnCostError, totalErrorSoFar, CheckGlobal(minimum.ColumnCostError), SolutionType.EveryCombination);
                    }
            return !minimum.KnownGlobal;
        }
        else
        {
            // start position
            var total = colTotal[col];
            for (int i = 0; i < rowTotal.Length; i++)
            {
                var row = solution[i];
                var rowRemaining = rowTotal[i] - row.SumStart(col + 1);
                if (total > rowRemaining)
                {
                    row[col] = rowRemaining;
                    total -= rowRemaining;
                }
                else
                {
                    row[col] = total;
                    total = 0;
                }
            }

            while (true)
            {
                if (minimum.KnownGlobal || finishTime <= Stopwatch.GetTimestamp())
                    return false;
                // calculate errors
                var colError_col = ColCostError(rowPrice, colTotal, solution, totalCost, totalQuantity, col);
                var additionalError = Sqr((double)colError_col / colTotal[col]);
                if (totalErrorSoFar + additionalError < minimum.TotalSquaredError)
                {
                    colErrorSoFar[col] = colError_col;
                    var completed = EveryCombination(rowPrice, rowTotal, colTotal, totalQuantity, totalCost,
                        solution, col - 1, colErrorSoFar, totalErrorSoFar + additionalError, finishTime, ref minimum);
                    if (!completed) return false;
                }
                var noProgress = true;
                for (int i = 1; i < solution.Length; i++)
                {
                    if (solution[i - 1][col] > 0 && solution[i].SumStart(col) < rowTotal[i])
                    {
                        solution[i - 1][col]--;
                        solution[i][col]++;
                        // reset start position to i - 1
                        total = 0;
                        for (int i2 = 0; i2 < i; i2++)
                            total += solution[i2][col];
                        for (int i2 = 0; i2 < i; i2++)
                        {
                            var row = solution[i2];
                            var rowRemaining = rowTotal[i2] - row.SumStart(col + 1);
                            if (total > rowRemaining)
                            {
                                row[col] = rowRemaining;
                                total -= rowRemaining;
                            }
                            else
                            {
                                row[col] = total;
                                total = 0;
                            }
                        }
                        noProgress = false;
                        break;
                    }
                }
                if (noProgress) break;
            }
            return true;
        }
    }

    private static Result RoundingSolutionThenFindLocalMinimum(int[] rowPrice, int[] rowTotal, int[] colTotal)
    {
        var results = RoundingSolution(rowTotal, colTotal);
        var colError = ColCostError(rowPrice, rowTotal, colTotal, results);
        FindLocalMinimum(rowPrice, colTotal, results, colError);
        var minError = TotalSquaredError(colError, colTotal);
        return new Result(results, colError, minError, CheckGlobal(colError), SolutionType.RoundingMinimum);
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