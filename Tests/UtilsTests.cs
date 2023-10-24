namespace Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CsCheck;
using Xunit;

public class UtilsTests
{
    [Fact]
    public void Equal()
    {
        Assert.True(Check.Equal(new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } }, new Dictionary<int, byte> { { 3, 4 }, { 1, 2 } }));
        Assert.True(Check.Equal([(1, 2), (3, 4)], new[] { (1, 2), (3, 4) }));
        Assert.False(Check.Equal([(1, 2), (3, 4)], new[] { (3, 4), (1, 2) }));
    }

    [Fact]
    public void ModelEqual()
    {
        Assert.True(Check.ModelEqual(new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) }));
        Assert.True(Check.ModelEqual(new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }, new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }));
        Assert.False(Check.ModelEqual(new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) }));
    }

    [Fact]
    public void Print()
    {
        Assert.Equal("[(1, 2), (3, 4)]", Check.Print(new KeyValuePair<int, int>[] { new(1, 2), new(3, 4) }));
        Assert.Equal("[(1, 2), (3, 4)]", Check.Print(new Tuple<int, int>[] { new(1, 2), new(3, 4) }));
        Assert.Equal("[(1, 2), (3, 4)]", Check.Print(new[] { (1, 2), (3, 4) }));
    }

    [Fact]
    public void PrintDouble()
    {
        Assert.Equal("0", Check.Print(0d));
        Assert.Equal("1", Check.Print(1d));
        Assert.Equal("1d/3", Check.Print(1d / 3));
        Assert.Equal("4d/3", Check.Print(4d / 3));
        Assert.Equal("17d/13", Check.Print(17d / 13));
        Assert.Equal("1E-20", Check.Print(1E-20));
        Assert.Equal("1234E20", Check.Print(1234E20));
    }

    [Fact]
    public void PrintFloat()
    {
        Assert.Equal("0", Check.Print(0f));
        Assert.Equal("1", Check.Print(1f));
        Assert.Equal("1f/3", Check.Print(1f / 3));
        Assert.Equal("4f/3", Check.Print(4f / 3));
        Assert.Equal("17f/13", Check.Print(17f / 13));
        Assert.Equal("1234E20", Check.Print(1234E20f));
    }

    [Fact]
    public void PrintDecimal()
    {
        Assert.Equal("0", Check.Print(0m));
        Assert.Equal("1", Check.Print(1m));
        Assert.Equal("1m/3", Check.Print(1m / 3));
        Assert.Equal("4m/3", Check.Print(4m / 3));
        Assert.Equal("17m/13", Check.Print(17m / 13));
        Assert.Equal("1E-20", Check.Print(1E-20m));
        Assert.Equal("1234E20", Check.Print(1234E20m));
    }
}

public class ThreadStatsTests
{
    static void Test(int[] ids, IEnumerable<int[]> expected)
    {
        var seq = new int[ids.Length];
        Array.Copy(ids, seq, ids.Length);
        Assert.Equal(expected, Check.Permutations(ids, seq), IntArrayComparer.Default);
    }

    [Fact]
    public void Permutations_11()
    {
        Test([1, 1], new int[][] {
            [1, 1],
        });
    }

    [Fact]
    public void Permutations_12()
    {
        Test([1, 2], new int[][] {
            [1, 2],
            [2, 1],
        });
    }

    [Fact]
    public void Permutations_112()
    {
        Test([1, 1, 2], new int[][] {
            [1, 1, 2],
            [1, 2, 1],
        });
    }

    [Fact]
    public void Permutations_121()
    {
        Test([1, 2, 1], new int[][] {
            [1, 2, 1],
            [2, 1, 1],
            [1, 1, 2],
        });
    }

    [Fact]
    public void Permutations_123()
    {
        Test([1, 2, 3], new int[][] {
            [1, 2, 3],
            [2, 1, 3],
            [1, 3, 2],
            [3, 1, 2],
            [2, 3, 1],
            [3, 2, 1],
        });
    }

    [Fact]
    public void Permutations_1212()
    {
        Test([1, 2, 1, 2], new int[][] {
            [1, 2, 1, 2],
            [2, 1, 1, 2],
            [1, 1, 2, 2],
            [1, 2, 2, 1],
            [2, 1, 2, 1],
        });
    }

    [Fact]
    public void Permutations_1231()
    {
        Test([1, 2, 3, 1], new int[][] {
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
        });
    }

    [Fact]
    public void Permutations_1232()
    {
        Test([1, 2, 3, 2], new int[][] {
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
        });
    }

    [Fact]
    public void Permutations_Should_Be_Unique()
    {
        Gen.Int[0, 5].Array[0, 10]
        .Sample(a =>
        {
            var a2 = new int[a.Length];
            Array.Copy(a, a2, a.Length);
            var ps = Check.Permutations(a, a2).ToList();
            var ss = new HashSet<int[]>(ps, IntArrayComparer.Default);
            Assert.Equal(ss.Count, ps.Count);
        });
    }
}

public class IntArrayComparer : IEqualityComparer<int[]>, IComparer<int[]>
{
    public readonly static IntArrayComparer Default = new();
    public int Compare(int[] x, int[] y)
    {
        for (int i = 0; i < x.Length; i++)
        {
            int c = x[i].CompareTo(y[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public bool Equals(int[] x, int[] y)
    {
        if (x.Length != y.Length) return false;
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