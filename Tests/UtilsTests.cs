namespace Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CsCheck;

public class UtilsTests
{
    [Test]
    public async Task Equal()
    {
        await Assert.That(Check.Equal(new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } }, new Dictionary<int, byte> { { 3, 4 }, { 1, 2 } })).IsTrue();
        await Assert.That(Check.Equal([(1, 2), (3, 4)], new[] { (1, 2), (3, 4) })).IsTrue();
        await Assert.That(Check.Equal([(1, 2), (3, 4)], new[] { (3, 4), (1, 2) })).IsFalse();
    }

    [Test]
    public async Task ModelEqual()
    {
        await Assert.That(Check.ModelEqual(new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) })).IsTrue();
        await Assert.That(Check.ModelEqual(new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }, new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) })).IsTrue();
        await Assert.That(Check.ModelEqual(new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) })).IsFalse();
    }

    [Test]
    public async Task Print()
    {
        await Assert.That(Check.Print(new KeyValuePair<int, int>[] { new(1, 2), new(3, 4) })).IsEqualTo("[[1, 2], [3, 4]]");
        await Assert.That(Check.Print(new Tuple<int, int>[] { new(1, 2), new(3, 4) })).IsEqualTo("[(1, 2), (3, 4)]");
        await Assert.That(Check.Print(new[] { (1, 2), (3, 4) })).IsEqualTo("[(1, 2), (3, 4)]");
    }

    [Test]
    public async Task PrintDouble()
    {
        await Assert.That(Check.Print(0d)).IsEqualTo("0");
        await Assert.That(Check.Print(1d)).IsEqualTo("1");
        await Assert.That(Check.Print(1d / 3)).IsEqualTo("1d/3");
        await Assert.That(Check.Print(4d / 3)).IsEqualTo("4d/3");
        await Assert.That(Check.Print(17d / 13)).IsEqualTo("17d/13");
        await Assert.That(Check.Print(1E-20)).IsEqualTo("1E-20");
        await Assert.That(Check.Print(1234E20)).IsEqualTo("1234E20");
    }

    [Test]
    public async Task PrintFloat()
    {
        await Assert.That(Check.Print(0f)).IsEqualTo("0");
        await Assert.That(Check.Print(1f)).IsEqualTo("1");
        await Assert.That(Check.Print(1f / 3)).IsEqualTo("1f/3");
        await Assert.That(Check.Print(4f / 3)).IsEqualTo("4f/3");
        await Assert.That(Check.Print(17f / 13)).IsEqualTo("17f/13");
        await Assert.That(Check.Print(1234E20f)).IsEqualTo("1234E20");
    }

    [Test]
    public async Task PrintDecimal()
    {
        await Assert.That(Check.Print(0m)).IsEqualTo("0");
        await Assert.That(Check.Print(1m)).IsEqualTo("1");
        await Assert.That(Check.Print(1m / 3)).IsEqualTo("1m/3");
        await Assert.That(Check.Print(4m / 3)).IsEqualTo("4m/3");
        await Assert.That(Check.Print(17m / 13)).IsEqualTo("17m/13");
        await Assert.That(Check.Print(1E-20m)).IsEqualTo("1E-20");
        await Assert.That(Check.Print(1234E20m)).IsEqualTo("1234E20");
    }
}

public class ThreadStatsTests
{
    static async Task Test(int[] ids, IEnumerable<int[]> expected)
    {
        var seq = new int[ids.Length];
        Array.Copy(ids, seq, ids.Length);
        await Assert.That(Check.Equal(Check.Permutations(ids, seq), expected)).IsTrue();
    }

    [Test]
    public async Task Permutations_11()
    {
        await Test([1, 1], [
            [1, 1],
        ]);
    }

    [Test]
    public async Task Permutations_12()
    {
        await Test([1, 2], [
            [1, 2],
            [2, 1],
        ]);
    }

    [Test]
    public async Task Permutations_112()
    {
        await Test([1, 1, 2], [
            [1, 1, 2],
            [1, 2, 1],
        ]);
    }

    [Test]
    public async Task Permutations_121()
    {
        await Test([1, 2, 1], [
            [1, 2, 1],
            [2, 1, 1],
            [1, 1, 2],
        ]);
    }

    [Test]
    public async Task Permutations_123()
    {
        await Test([1, 2, 3], [
            [1, 2, 3],
            [2, 1, 3],
            [1, 3, 2],
            [3, 1, 2],
            [2, 3, 1],
            [3, 2, 1],
        ]);
    }

    [Test]
    public async Task Permutations_1212()
    {
        await Test([1, 2, 1, 2], [
            [1, 2, 1, 2],
            [2, 1, 1, 2],
            [1, 1, 2, 2],
            [1, 2, 2, 1],
            [2, 1, 2, 1],
        ]);
    }

    [Test]
    public async Task Permutations_1231()
    {
        await Test([1, 2, 3, 1], [
            [1, 2, 3, 1],
            [2, 1, 3, 1],
            [1, 3, 2, 1],
            [3, 1, 2, 1],
            [1, 2, 1, 3],
            [1, 1, 2, 3],
            [2, 3, 1, 1],
            [2, 1, 1, 3],
            [1, 3, 1, 2],
            [3, 2, 1, 1],
            [3, 1, 1, 2],
            [1, 1, 3, 2],
        ]);
    }

    [Test]
    public async Task Permutations_1232()
    {
        await Test([1, 2, 3, 2], [
            [1, 2, 3, 2],
            [2, 1, 3, 2],
            [1, 3, 2, 2],
            [3, 1, 2, 2],
            [1, 2, 2, 3],
            [2, 3, 1, 2],
            [2, 1, 2, 3],
            [2, 2, 1, 3],
            [3, 2, 1, 2],
            [2, 3, 2, 1],
            [2, 2, 3, 1],
            [3, 2, 2, 1],
        ]);
    }

    [Test]
    public void Permutations_Should_Be_Unique()
    {
        Gen.Int[0, 5].Array[0, 10]
        .Sample(a =>
        {
            var a2 = new int[a.Length];
            Array.Copy(a, a2, a.Length);
            var ps = Check.Permutations(a, a2).ToList();
            var ss = new HashSet<int[]>(ps, IntArrayComparer.Default);
            return ss.Count == ps.Count;
        });
    }

    [Test]
    public async Task BigO_Exact_Examples()
    {
        await Assert.That(Check.BigO([1, 2, 3], [5, 5, 5])).IsEqualTo(BigO.Constant);
        await Assert.That(Check.BigO([1, 2, 3], [5, 6, 7])).IsEqualTo(BigO.Linear);
        await Assert.That(Check.BigO([1, 2, 3], [5, 8, 13])).IsEqualTo(BigO.Quadratic);
        await Assert.That(Check.BigO([1, 2, 3], [1, 8, 27])).IsEqualTo(BigO.Cubic);
        await Assert.That(Check.BigO([1, 2, 3], [1, 1 + Math.Log(2), 1 + Math.Log(3)])).IsEqualTo(BigO.Logarithmic);
        await Assert.That(Check.BigO([1, 2, 3], [1, 1 + 2 * Math.Log(2), 1 + 3 * Math.Log(3)])).IsEqualTo(BigO.Linearithmic);
        await Assert.That(Check.BigO([1, 2, 3], [4, 8, 16])).IsEqualTo(BigO.Exponential);
    }
}

public class IntArrayComparer : IEqualityComparer<int[]>, IComparer<int[]>
{
    public readonly static IntArrayComparer Default = new();
    public int Compare(int[]? x, int[]? y)
    {
        for (int i = 0; i < x!.Length; i++)
        {
            int c = x[i].CompareTo(y![i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public bool Equals(int[]? x, int[]? y)
    {
        if (x!.Length != y!.Length) return false;
        for (int i = 0; i < x.Length; i++)
            if (x[i] != y[i]) return false;
        return true;
    }

    public int GetHashCode([DisallowNull] int[] a)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (int i in a)
                hash = (hash * 16777619) ^ i;
            return hash;
        }
    }
}

public class Phase
{
    public string PhaseName { get; set; } = string.Empty;
    public Experiment? LatestExperiment { get; set; }
}

public class Experiment
{
    public string ExperimentName {  get; set; } = string.Empty;
}

public class ResearchProject
{
    public Phase? ExperimentalPhase { get; set; }
}