namespace Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

    static List<string> OrderPreservingInterleavings(int[] threadIds)
    {
        var results = new List<string>();
        var taken = new bool[threadIds.Length];
        var order = new List<int>();
        void Recurse()
        {
            if (order.Count == threadIds.Length) { results.Add(string.Join(",", order)); return; }
            for (int i = 0; i < threadIds.Length; i++)
            {
                if (taken[i] || Enumerable.Range(0, i).Any(j => !taken[j] && threadIds[j] == threadIds[i])) continue;
                taken[i] = true;
                order.Add(i);
                Recurse();
                order.RemoveAt(order.Count - 1);
                taken[i] = false;
            }
        }
        Recurse();
        return results;
    }

    [Test]
    public void Permutations_Matches_Interleaving_Oracle()
    {
        Gen.Int[0, 2].Array[1, 6].Sample(threadIds =>
        {
            var sequence = Enumerable.Range(0, threadIds.Length).ToArray();
            var actual = Check.Permutations((int[])threadIds.Clone(), sequence)
                .Select(p => string.Join(",", p)).ToList();
            var expected = OrderPreservingInterleavings(threadIds);
            return actual.Count == expected.Count && actual.ToHashSet().SetEquals(expected);
        });
    }

    static void Rejects<T>(string name, Gen<T> invalid, Action<T> set, Func<T> get)
    {
        var before = get();
        invalid.Sample(v =>
            Assert.Throws<CsCheckException>(() => set(v))!.Message.StartsWith($"Check.{name} (CsCheck_{name}) must be ", StringComparison.Ordinal)
            && Check.Equal(get(), before));
    }

    [Test]
    public async Task Settings_Reject_Values_That_Run_Nothing_Or_Hang()
    {
        Rejects("Iter", Gen.OneOf(Gen.Const(0L), Gen.Long[long.MinValue, 0]), v => Check.Iter = v, () => Check.Iter);
        Rejects("Time", Gen.OneOf(Gen.Const(0), Gen.Const(-2), Gen.Int[int.MinValue, 0].Where(t => t != -1)), v => Check.Time = v, () => Check.Time);
        Rejects("Replay", Gen.OneOf(Gen.Const(-1), Gen.Int[int.MinValue, -1]), v => Check.Replay = v, () => Check.Replay);
        Rejects("Threads", Gen.OneOf(Gen.Const(0), Gen.Int[int.MinValue, 0]), v => Check.Threads = v, () => Check.Threads);
        Rejects("Sigma", Gen.OneOf(Gen.Const(0.0), Gen.Const(double.NaN), Gen.Const(double.PositiveInfinity), Gen.Double[-1e300, 0]), v => Check.Sigma = v, () => Check.Sigma);
        Rejects("Timeout", Gen.OneOf(Gen.Const(-1), Gen.Int[int.MinValue, -1]), v => Check.Timeout = v, () => Check.Timeout);
        Rejects("Ulps", Gen.OneOf(Gen.Const(-1), Gen.Int[int.MinValue, -1]), v => Check.Ulps = v, () => Check.Ulps);
        Rejects("WhereLimit", Gen.OneOf(Gen.Const(0), Gen.Int[int.MinValue, 0]), v => Check.WhereLimit = v, () => Check.WhereLimit);
        Rejects("SingleLimit", Gen.OneOf(Gen.Const(0), Gen.Int[int.MinValue, 0]), v => Check.SingleLimit = v, () => Check.SingleLimit);
        var seed = Check.Seed;
        foreach (var invalid in new[] { "abc", "0N0XIzNsQ0O", "0N0XIzNsQ0O2!" })
            Assert.Throws<CsCheckException>(() => Check.Seed = invalid);
        await Assert.That(Check.Seed).IsEqualTo(seed);
    }

    [Test]
    public async Task Environment_Variables_Use_The_Invariant_Culture_And_Fail_Loudly()
    {
        var variable = $"CsCheck_Test_{Guid.NewGuid():N}";
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Gen.Double.Sample(d =>
            {
                Environment.SetEnvironmentVariable(variable, d.ToString("R", CultureInfo.InvariantCulture));
                return CultureInfo.CurrentCulture.Name == "de-DE" && Check.ParseEnvironmentVariableToDouble(variable, 0).Equals(d);
            }, threads: 1);
            Environment.SetEnvironmentVariable(variable, " ");
            await Assert.That(Check.ParseEnvironmentVariableToInt(variable, 7)).IsEqualTo(7);
            foreach (var (value, expected) in new[] { ("yes", true), (" No ", false), ("Y", true), ("n", false), ("TRUE", true), ("false", false), ("1", true), ("0", false) })
            {
                Environment.SetEnvironmentVariable(variable, value);
                await Assert.That(Check.ParseEnvironmentVariableToBool(variable, !expected)).IsEqualTo(expected);
            }
            foreach (var (value, parse, expected) in new (string, Action, string)[]
            {
                ("6,5", () => Check.ParseEnvironmentVariableToDouble(variable, 0), "a number"),
                ("1.5", () => Check.ParseEnvironmentVariableToInt(variable, 0), "an integer"),
                ("abc", () => Check.ParseEnvironmentVariableToLong(variable, 0), "an integer"),
                ("maybe", () => Check.ParseEnvironmentVariableToBool(variable, false), "true, false, yes, no, y, n, 1 or 0"),
            })
            {
                Environment.SetEnvironmentVariable(variable, value);
                await Assert.That(Assert.Throws<CsCheckException>(parse)!.Message).IsEqualTo($"{variable} must be {expected}, but was \"{value}\".");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}

public class ThreadStatsTests
{
    static async Task Test(int[] ids, IEnumerable<int[]> expected)
    {
        var seq = new int[ids.Length];
        Array.Copy(ids, seq, ids.Length);
        await Assert.That(Check.EqualUnordered(Check.Permutations(ids, seq), expected)).IsTrue();
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
            [2, 1, 1],
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
            [2, 2, 1, 1],
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

    /// <summary>The complexity class cannot depend on the unit the times were measured in.</summary>
    [Test]
    [Arguments(1e-6)]
    [Arguments(1.0)]
    [Arguments(1e3)]
    [Arguments(1e6)]
    [Arguments(1e9)]
    public async Task BigO_Is_Scale_Invariant(double scale)
    {
        static double[] Scaled(double[] times, double by) => Array.ConvertAll(times, t => t * by);
        double[] n = [1, 2, 3];
        await Assert.That(Check.BigO(n, Scaled([5, 5, 5], scale))).IsEqualTo(BigO.Constant);
        await Assert.That(Check.BigO(n, Scaled([5, 6, 7], scale))).IsEqualTo(BigO.Linear);
        await Assert.That(Check.BigO(n, Scaled([5, 8, 13], scale))).IsEqualTo(BigO.Quadratic);
        await Assert.That(Check.BigO(n, Scaled([1, 8, 27], scale))).IsEqualTo(BigO.Cubic);
        await Assert.That(Check.BigO(n, Scaled([1, 1 + Math.Log(2), 1 + Math.Log(3)], scale))).IsEqualTo(BigO.Logarithmic);
        await Assert.That(Check.BigO(n, Scaled([1, 1 + 2 * Math.Log(2), 1 + 3 * Math.Log(3)], scale))).IsEqualTo(BigO.Linearithmic);
        await Assert.That(Check.BigO(n, Scaled([4, 8, 16], scale))).IsEqualTo(BigO.Exponential);
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