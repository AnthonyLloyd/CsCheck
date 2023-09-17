namespace Tests;

using System;
using System.Linq;
using CsCheck;
using Xunit;

public class AllocatorMany_Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    readonly Action<string> writeLine = output.WriteLine;

    [Fact]
    public void RoundingSolutionTest()
    {
        Gen.Select(
            Gen.Int[1, 1000].Array[2, 200],
            Gen.Int[1, 1000].Cast<double>().Array[2, 20])
        .Sample((rowTotal, colWeight) =>
        {
            var colTotal = Allocator.Allocate(rowTotal.Sum(), colWeight);
            var results = AllocatorMany.RoundingSolution(rowTotal, colTotal);
            return TotalsCorrectly(rowTotal, colTotal, results);
        });
    }

    [Fact]
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

    [Fact]
    public void Random()
    {
        Gen.Select(Gen.Int[1, 10], Gen.Int[1, 10]).SelectMany((I, J) =>
            Gen.Select(
                Gen.Int[0, 5].Array[J].Where(a => a.Sum() > 0).Array[I],
                Gen.Int[0, 10].Array[I],
                Gen.Int.Uniform))
        .Sample((solution,
                 rowPrice,
                 seed) =>
        {
            var rowTotal = Array.ConvertAll(solution, x => x.Sum());
            var colTotal = new int[solution[0].Length];
            for (int j = 0; j < colTotal.Length; j++)
                colTotal[j] = solution.SumCol(j);
            var allocation = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, new Random(seed), 1);
            if (!TotalsCorrectly(rowTotal, colTotal, allocation.Solution))
                throw new Exception("Does not total correctly");
            return $"{(allocation.KnownGlobal ? "Global" : "Local")}/{allocation.SolutionType}";
        }, time: 10, threads: 1, writeLine: writeLine);
    }

    [Fact]
    public void Simple()
    {
        var actual = AllocatorMany.Allocate([9, 2, 1], [1, 20, 20], [21, 0, 10, 0, 10], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.RoundingMinimum, actual.SolutionType);
        Assert.Equal([[1, 0, 0, 0, 0], [6, 0, 7, 0, 7], [14, 0, 3, 0, 3]], actual.Solution);
    }

    [Fact]
    public void Example01()
    {
        var actual = AllocatorMany.Allocate([96625, 96620], [4, 6], [4, 1, 1, 1, 1, 1, 1], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.EveryCombination, actual.SolutionType);
        Assert.Equal([[3, 1, 0, 0, 0, 0, 0], [1, 0, 1, 1, 1, 1, 1]], actual.Solution);
    }

    [Fact]
    public void Example02()
    {
        var actual = AllocatorMany.Allocate([12, 11], [50, 30], [20, 60], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.RoundingMinimum, actual.SolutionType);
        Assert.Equal([[12, 38], [8, 22]], actual.Solution);
    }

    [Fact]
    public void Example03()
    {
        var actual = AllocatorMany.Allocate([12, 11, 15], [56, 42, 14], [28, 63, 21], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.RoundingMinimum, actual.SolutionType);
        Assert.Equal([[16, 31, 9], [9, 24, 9], [3, 8, 3]], actual.Solution);
    }

    [Fact]
    public void Example04()
    {
        var actual = AllocatorMany.Allocate(
            [3175, 3174, 3173, 3170, 3169, 3168, 3167],
            [2, 3, 1, 1, 1, 2, 1],
            [4, 1, 1, 1, 1, 1, 1, 1], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.EveryCombination, actual.SolutionType);
        Assert.Equal([
            [2, 0, 0, 0, 0, 0, 0, 0],
            [0, 1, 1, 1, 0, 0, 0, 0],
            [0, 0, 0, 0, 1, 0, 0, 0],
            [0, 0, 0, 0, 0, 1, 0, 0],
            [0, 0, 0, 0, 0, 0, 1, 0],
            [1, 0, 0, 0, 0, 0, 0, 1],
            [1, 0, 0, 0, 0, 0, 0, 0],
        ], actual.Solution);
    }

    [Fact]
    public void Example05()
    {
        var actual = AllocatorMany.Allocate(
            [37060, 37073, 37748, 38051],
            [586, 1055, 7183, 5560],
            [2744, 413, 524, 10582, 121], new(123), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.RandomChange, actual.SolutionType);
    }

    [Fact]
    public void Example06()
    {
        var actual = AllocatorMany.Allocate(
            [127584, 127678, 128097, 128157, 128483],
            [1, 1, 1, 1, 1],
            [0, 1, 1, 0, 0, 1, 0, 1, 1, 0], new(), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.OnesColumn, actual.SolutionType);
    }

    [Fact]
    public void Example07()
    {
        var actual = AllocatorMany.Allocate(
            [34378, 34506, 34535],
            [3900, 800, 400],
            [900, 400, 3800], new(123), 100);
        Assert.True(actual.KnownGlobal);
    }

    [Fact]
    public void Example08()
    {
        var actual = AllocatorMany.Allocate(
            [18880, 18916, 18920],
            [1861271, 61527, 67534],
            [381549, 56645, 69287, 1466297, 16554], new(123), 2);
        Assert.False(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.RandomChange, actual.SolutionType);
    }

    [Fact]
    public void Example09()
    {
        var actual = AllocatorMany.Allocate(
            [96030, 96050, 96040, 95055, 96040, 96035, 96035, 96035],
            [1, 1, 1, 1, 1, 1, 1, 1],
            [1, 5, 2], new(123), 100);
        Assert.True(actual.KnownGlobal);
        Assert.Equal(AllocatorMany.SolutionType.EveryCombination, actual.SolutionType);
        Assert.Equal([
            [1, 0, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 1, 0],
            [0, 0, 1],
            [0, 0, 1],
        ], actual.Solution);
    }

    [Fact]
    public void Example10()
    {
        Gen.Int[2, 20].SelectMany(rows =>
            Gen.Select(
                Gen.Int[1, 300].Array[rows],
                Gen.Int[1, 100].Array[rows],
                Gen.Int[1, 1000].Cast<double>().Array[2, 10],
                Gen.Int.Uniform))
        .Sample((rowPrice, rowTotal, weight, seed) =>
        {
            var random = new Random(seed);
            var colTotal = Allocator.Allocate(rowTotal.Sum(), weight);
            var allocation = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, random, 1);
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

    [Fact]
    public void Example_139x19()
    {
        var fills = new[] {
            (331,1), (350,2), (357,1), (360,3), (366,2), (371,1), (373,1), (375,3), (376,1), (377,2), (378,2), (379,2), (381,2),
            (383,3), (384,2), (385,5), (386,2), (387,1), (389,3), (390,2), (391,1), (392,6), (393,3), (394,3), (395,10), (396,7),
            (397,6), (398,2), (399,5), (400,3), (401,1), (403,4), (404,4), (405,3), (406,2), (407,3), (408,5), (409,3), (410,2),
            (411,1), (415,1), (419,7), (421,1), (424,2), (425,2), (426,2), (436,3), (437,2), (438,1), (446,1), (447,2)
        };
        Assert.Equal(139, fills.Sum(i => i.Item2));
        Assert.Equal(fills.Select(i => i.Item1), fills.Select(i => i.Item1).Distinct());
        var accounts = new int[] { 2, 2, 2, 2, 2, 3, 5, 5, 6, 7, 11, 12, 13, 17, 50 };
        Assert.Equal(139, accounts.Sum());
        var p = Array.ConvertAll(fills, i => i.Item1);
        var q = Array.ConvertAll(fills, i => i.Item2);
        var actual = AllocatorMany.Allocate(p, q, accounts, new Random(123), 10);
    }

    [Fact(Skip = "remove?")]
    public void AllocateTest()
    {
        Gen.Int[2, 20].SelectMany(rows =>
            Gen.Select(
                Gen.Int[1, 300].Array[rows],
                Gen.Int[1, 100].Array[rows],
                Gen.Int[1, 1000].Cast<double>().Array[2, 10],
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
}
