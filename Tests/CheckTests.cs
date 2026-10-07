namespace Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CsCheck;

public class CheckTests
{
    static void Assert_Commutative<T, R>(Gen<T> gen, Func<T, T, R> operation)
    {
        Gen.Select(gen, gen)
        .Sample((op1, op2) => operation(op1, op2)!.Equals(operation(op2, op1)));
    }

    [Test]
    public void Sample_Addition_Is_Commutative()
    {
        Assert_Commutative(Gen.Byte, (x, y) => x + y);
        Assert_Commutative(Gen.SByte, (x, y) => x + y);
        Assert_Commutative(Gen.UShort, (x, y) => x + y);
        Assert_Commutative(Gen.Short, (x, y) => x + y);
        Assert_Commutative(Gen.UInt, (x, y) => x + y);
        Assert_Commutative(Gen.Int, (x, y) => x + y);
        Assert_Commutative(Gen.ULong, (x, y) => x + y);
        Assert_Commutative(Gen.Long, (x, y) => x + y);
        Assert_Commutative(Gen.Single, (x, y) => x + y);
        Assert_Commutative(Gen.Double, (x, y) => x + y);
    }

    [Test]
    public void Sample_Multiplication_Is_Commutative()
    {
        Assert_Commutative(Gen.Byte, (x, y) => x * y);
        Assert_Commutative(Gen.SByte, (x, y) => x * y);
        Assert_Commutative(Gen.UShort, (x, y) => x * y);
        Assert_Commutative(Gen.Short, (x, y) => x * y);
        Assert_Commutative(Gen.UInt, (x, y) => x * y);
        Assert_Commutative(Gen.Int, (x, y) => x * y);
        Assert_Commutative(Gen.ULong, (x, y) => x * y);
        Assert_Commutative(Gen.Long, (x, y) => x * y);
        Assert_Commutative(Gen.Single, (x, y) => x * y);
        Assert_Commutative(Gen.Double, (x, y) => x * y);
    }

    static void Assert_Associative<T>(Gen<T> gen, Func<T, T, T> operation)
    {
        Gen.Select(gen, gen, gen)
        .Sample((op1, op2, op3) =>
            operation(op1, operation(op2, op3))!.Equals(operation(operation(op1, op2), op3)));
    }

    [Test]
    public void Sample_Addition_Is_Associative()
    {
        Assert_Associative(Gen.UInt, (x, y) => x + y);
        Assert_Associative(Gen.Int, (x, y) => x + y);
        Assert_Associative(Gen.ULong, (x, y) => x + y);
        Assert_Associative(Gen.Long, (x, y) => x + y);
    }

    [Test]
    public void Sample_Multiplication_Is_Associative()
    {
        Assert_Associative(Gen.UInt, (x, y) => x * y);
        Assert_Associative(Gen.Int, (x, y) => x * y);
        Assert_Associative(Gen.ULong, (x, y) => x * y);
        Assert_Associative(Gen.Long, (x, y) => x * y);
    }

    static double[,] MulIJK(double[,] a, double[,] b)
    {
        int I = a.GetLength(0), J = a.GetLength(1), K = b.GetLength(1);
        var c = new double[I, K];
        for (int i = 0; i < I; i++)
        {
            for (int j = 0; j < J; j++)
            {
                for (int k = 0; k < K; k++)
                    c[i, k] += a[i, j] * b[j, k];
            }
        }

        return c;
    }

    static double[,] MulIKJ(double[,] a, double[,] b)
    {
        int I = a.GetLength(0), J = a.GetLength(1), K = b.GetLength(1);
        var c = new double[I, K];
        for (int i = 0; i < I; i++)
        {
            for (int k = 0; k < K; k++)
            {
                double t = 0.0;
                for (int j = 0; j < J; j++)
                    t += a[i, j] * b[j, k];
                c[i, k] = t;
            }
        }

        return c;
    }

    [Test]
    public void Faster_Matrix_Multiply_Fixed()
    {
        const int I = 30, J = 37, K = 29;
        var rand = new Random(42);
        var a = new double[I, J];
        for (int i = 0; i < I; i++)
        {
            for (int j = 0; j < J; j++)
                a[i, j] = rand.NextDouble();
        }

        var b = new double[J, K];
        for (int j = 0; j < J; j++)
        {
            for (int k = 0; k < K; k++)
                b[j, k] = rand.NextDouble();
        }

        Check.Faster(
            () => MulIKJ(a, b),
            () => MulIJK(a, b),
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void Faster_Matrix_Multiply_Range()
    {
        var genDim = Gen.Int[5, 30];
        var genArray = Gen.Double.Unit.Array2D;
        Gen.SelectMany(genDim, genDim, genDim, (i, j, k) => Gen.Select(genArray[i, j], genArray[j, k]))
        .Faster(
            MulIKJ,
            MulIJK,
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void Faster_Linq_Random()
    {
        Gen.Byte.Array[100, 1000]
        .Faster(
            data =>
            {
                double s = 0.0;
                foreach (var b in data) s += b;
                return s;
            },
            data => data.Aggregate(0.0, (t, b) => t + b),
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void Faster_CustomCriterion()
    {
        static bool SuccessCriterion(double output1, double output2) => output1 >= 0.7 * output2;

        Gen.Double[100, 1000]
            .Faster(
                d => d * 0.8,
                d =>
                {
                    Thread.Sleep(1);
                    return d;
                },
                equal: SuccessCriterion,
                writeLine: TUnitX.WriteLine);
    }

    [Test]
    public async Task Equal_Dictionary()
    {
        await Assert.That(Check.Equal(
            new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } },
            new Dictionary<int, byte> { { 3, 4 }, { 1, 2 } }
        )).IsTrue();
    }

    [Test]
    public async Task Equal_List()
    {
        await Assert.That(Check.Equal<List<int>>([1, 2, 3, 4], [1, 2, 3, 4])).IsTrue();
        await Assert.That(Check.Equal<List<int>>([1, 2, 3, 4], [1, 2, 4, 3])).IsFalse();
    }

    [Test]
    public async Task Equal_Array()
    {
        await Assert.That(Check.Equal<int[]>([1, 2, 3, 4], [1, 2, 3, 4])).IsTrue();
        await Assert.That(Check.Equal<int[]>([1, 2, 3, 4], [1, 2, 4, 3])).IsFalse();
    }

    [Test]
    public async Task Equal_Array2D()
    {
        await Assert.That(Check.Equal(
            new int[,] { { 1, 2 }, { 3, 4 } },
            new int[,] { { 1, 2 }, { 3, 4 } }
        )).IsTrue();
        await Assert.That(Check.Equal(
            new int[,] { { 1, 2 }, { 3, 4 } },
            new int[,] { { 1, 2 }, { 4, 3 } }
        )).IsFalse();
    }

    [Test]
    public async Task Equal_ConcurrentBag_Counts_Duplicates()
    {
        await Assert.That(Check.Equal<ConcurrentBag<int>>([with([1, 1, 2])], [with([2, 1, 1])])).IsTrue();
        await Assert.That(Check.Equal<ConcurrentBag<int>>([with([1, 1, 2])], [with([1, 2, 2])])).IsFalse();
    }

    [Test]
    public async Task EqualUnordered_Counts_Duplicates()
    {
        await Assert.That(Check.EqualUnordered(new[] { 1, 1, 2 }, new[] { 1, 2, 1 })).IsTrue();
        await Assert.That(Check.EqualUnordered(new[] { 1, 1, 2 }, new[] { 1, 2, 2 })).IsFalse();
    }

    [Test]
    public async Task Equal_Dictionary_Compares_Values_Structurally()
    {
        await Assert.That(Check.Equal(new Dictionary<int, int[]> { [1] = [2] }, new Dictionary<int, int[]> { [1] = [2] })).IsTrue();
        await Assert.That(Check.Equal(new Dictionary<int, int[]> { [1] = [2] }, new Dictionary<int, int[]> { [1] = [3] })).IsFalse();
        await Assert.That(Check.Equal<object?>(new ConcurrentDictionary<int, int[]>([new(1, [2])]), new Dictionary<int, int[]> { [1] = [2] })).IsTrue();
    }

    [Test]
    public async Task Equal_ImmutableArray_Compares_Elements()
    {
        await Assert.That(Check.Equal(ImmutableArray.Create(1, 2), [1, 2])).IsTrue();
        await Assert.That(Check.Equal(ImmutableArray.Create(1, 2), [2, 1])).IsFalse();
    }

    [Test]
    public async Task Equal_Array3D()
    {
        await Assert.That(Check.Equal(new int[,,] { { { 1, 2 } } }, new int[,,] { { { 1, 2 } } })).IsTrue();
        await Assert.That(Check.Equal(new int[,,] { { { 1, 2 } } }, new int[,,] { { { 2, 1 } } })).IsFalse();
    }

    [Test]
    public async Task Equal_ReadOnlySet_Is_Unordered()
    {
        await Assert.That(Check.Equal(new ReadOnlySet<int>(new HashSet<int> { 1, 2 }), new ReadOnlySet<int>(new HashSet<int> { 2, 1 }))).IsTrue();
    }

    [Test]
    public async Task Equal_List_Of_Strings()
    {
        await Assert.That(Check.Equal<List<string?>>(["ab", null], ["ab", null])).IsTrue();
        await Assert.That(Check.Equal<List<string?>>(["ab"], ["ac"])).IsFalse();
    }

    [Test]
    public async Task Equal_Actual_And_Model_Of_Different_Types()
    {
        await Assert.That(Check.Equal<object?>(new HashSet<int> { 1, 2, 3, 4 }, new List<int> { 4, 3, 2, 1 })).IsTrue();
        await Assert.That(Check.Equal<object?>(new List<int> { 1, 2, 3, 4 }, new[] { 1, 2, 3, 4 })).IsTrue();
        await Assert.That(Check.Equal<object?>(new List<int> { 1, 2, 3, 4 }, new[] { 1, 2, 4, 3 })).IsFalse();
        await Assert.That(Check.Equal<object?>(new Dictionary<int, byte> { [1] = 2, [3] = 4 }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) })).IsTrue();
        await Assert.That(Check.Equal<object?>(new KeyValuePair<int, byte>[] { new(1, 2), new(3, 4) }, new KeyValuePair<int, byte>[] { new(3, 4), new(1, 2) })).IsFalse();
    }

    [Test]
    public void Equal_Matches_Sequence_And_Multiset_Oracles()
    {
        static Dictionary<int, int[]> ToDict(int[] a) => a.Select((x, i) => (i, x)).ToDictionary(t => t.i, t => new[] { t.x });
        Gen.Select(Gen.Int[0, 3].Array[0, 6], Gen.Int[0, 3].Array[0, 6], Gen.Int[0, 2], (xs, other, kind) => (xs, ys: kind switch
        {
            0 => [.. xs.OrderDescending()],
            1 => [.. other.Concat(xs).Take(xs.Length)],
            _ => other,
        }))
        .Sample((xs, ys) =>
            Check.Equal<object?>(xs, ys.ToList()) == xs.SequenceEqual(ys)
            && Check.Equal<object?>(new ConcurrentBag<int>(xs), ys.ToList()) == xs.Order().SequenceEqual(ys.Order())
            && Check.Equal<object?>(xs.Select(x => new[] { x }).ToList(), ys.Select(y => new[] { y }).ToList()) == xs.SequenceEqual(ys)
            && Check.Equal<object?>(new ConcurrentDictionary<int, int[]>(ToDict(xs)), ToDict(ys)) == xs.SequenceEqual(ys));
    }

    /// <summary>Sample writes its passed line after the property has already succeeded, so a sink that throws there
    /// would turn a passing property into a failing test for a formatting problem.</summary>
    [Test]
    public void A_Throwing_WriteLine_Does_Not_Fail_A_Passing_Sample()
    {
        Gen.Int[0, 10].Sample(i => i >= 0, writeLine: _ => throw new InvalidOperationException("the sink is gone"), iter: 10);
    }

    static Action[] AllSampleKinds(Gen<int> gen, Func<int, bool> ok, long iter, string? seed = null) =>
    [
        () => gen.Sample(i => { if (!ok(i)) throw new ArgumentException("failed"); }, seed: seed, iter: iter),
        () => gen.Sample(ok, seed: seed, iter: iter),
        () => gen.SampleAsync(async i => { await Task.Yield(); if (!ok(i)) throw new ArgumentException("failed"); }, seed: seed, iter: iter).GetAwaiter().GetResult(),
        () => gen.SampleAsync(async i => { await Task.Yield(); return ok(i); }, seed: seed, iter: iter).GetAwaiter().GetResult(),
    ];

    [Test]
    public async Task Sample_Reports_A_Throwing_Generator_With_A_Seed_That_Reproduces_It()
    {
        var gen = Gen.Int[0, 100].Select(i => i < 50 ? i : throw new InvalidOperationException("gen bug"));
        for (int kind = 0; kind < 4; kind++)
        {
            var e = Assert.Throws<CsCheckException>(AllSampleKinds(gen, _ => true, 100)[kind]);
            await Assert.That(e.Message.Split('\n')[^1]).IsEqualTo("The generator threw.");
            await Assert.That(e.InnerException is InvalidOperationException).IsTrue();
            var seed = e.Message.Split('"')[1];
            Assert.Throws<InvalidOperationException>(() => gen.Generate(PCG.Parse(seed), null, out _));
            var replay = Assert.Throws<CsCheckException>(AllSampleKinds(gen, _ => true, 1, seed)[kind]);
            await Assert.That(replay.Message.Split('\n')[^1]).IsEqualTo("The generator threw.");
        }
    }

    [Test]
    public async Task Sample_Shrinks_Past_A_Throwing_Generator()
    {
        var gen = Gen.Int[0, 100].Select(i => i == 7 ? throw new InvalidOperationException("gen bug") : i);
        foreach (var sample in AllSampleKinds(gen, i => i < 90, 10_000))
        {
            var e = Assert.Throws<CsCheckException>(sample);
            await Assert.That(e.Message.Split('\n')[^1]).IsEqualTo("90");
            await Assert.That(e.InnerException is InvalidOperationException).IsFalse();
        }
    }

    /// <summary>Arrays of different rank are unequal rather than throwing out of the rank 2 comparison.</summary>
    [Test]
    public async Task Equal_Arrays_Of_Different_Rank_Are_Unequal()
    {
        object oneD = new[] { 1, 2 };
        object twoD = new[,] { { 1, 2 } };
        await Assert.That(Check.Equal(twoD, oneD)).IsFalse();
        await Assert.That(Check.Equal(oneD, twoD)).IsFalse();
        await Assert.That(Check.Equal(twoD, new[,] { { 1, 2 } })).IsTrue();
        await Assert.That(Check.Equal(oneD, new[] { 1, 2 })).IsTrue();
    }

    [Test]
    public async Task Equal_Array2D_Compares_Elements_Structurally()
    {
        await Assert.That(Check.Equal(
            new List<int>[,] { { [1], [2] } },
            new List<int>[,] { { [1], [2] } }
        )).IsTrue();
        await Assert.That(Check.Equal(
            new List<int>[,] { { [1], [2] } },
            new List<int>[,] { { [1], [3] } }
        )).IsFalse();
    }

    [Test]
    public void SampleModelBased_ConcurrentBag()
    {
        Gen.Int[0, 5].List.Select(l => (new ConcurrentBag<int>(l), l))
        .SampleModelBased(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
            Gen.Operation<ConcurrentBag<int>, List<int>>(bag => bag.TryTake(out _), list => { if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count
        , threads: 1);
    }

    [Test]
    public async Task SampleModelBasedAsync_ConcurrentBag()
    {
        await Gen.Int[0, 5].List.Select(l => Task.FromResult((new ConcurrentBag<int>(l), l)))
        .SampleModelBasedAsync(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>(async (bag, i) => { await Task.Yield(); bag.Add(i); }, async (list, i) => { await Task.Yield(); list.Add(i); }),
            Gen.Operation<ConcurrentBag<int>, List<int>>(async bag => { await Task.Yield(); bag.TryTake(out _); }, async list => { await Task.Yield(); if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count
        , threads: 1);
    }

    /// <summary>The question a model-based run cannot otherwise answer: did each operation ever meet the states that
    /// make it interesting. Here that is whether TryTake ran on an empty bag as well as a full one, which decides
    /// whether the equal check ever compared anything but the easy case. The initial list is bounded because the
    /// default Count is uniform over 0 to 127, so a bag starting near 64 with balanced adds and takes reaches empty
    /// only in the rare iteration that starts there. That is the finding this table is for.</summary>
    [Test]
    public async Task SampleModelBased_Classify()
    {
        var lines = new List<string>();
        Gen.Int[0, 5].List[0, 3].Select(l => (new ConcurrentBag<int>(l), l))
        .SampleModelBased(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
            Gen.Operation<ConcurrentBag<int>, List<int>>(bag => bag.TryTake(out _), list => { if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count, threads: 1,
            classify: list => list.Count == 0 ? "empty" : "non-empty", writeLine: lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        await Assert.That(lines.Any(l => l.Contains("| Op0"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("| Op1"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains(Leaf("empty")))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains(Leaf("non-empty")))).IsTrue();
        // Counts only, the operations are not timed.
        await Assert.That(lines.Any(l => l.Contains("Median"))).IsFalse();
    }

    /// <summary>Counted per thread and merged, so every operation is counted once, and Op1's row does not take in Op10's.</summary>
    [Test]
    public async Task SampleModelBased_Classify_Counts_Every_Operation_Once()
    {
        var ran = 0;
        var lines = new List<string>();
        Gen.Const(() => (new List<int>(), new List<int>()))
        .SampleModelBased(
            [.. Enumerable.Range(0, 11).Select(i => Gen.Operation<List<int>, List<int>>(a => { Interlocked.Increment(ref ran); a.Add(i); }, m => m.Add(i)))],
            classify: m => m.Count % 2 == 0 ? "even" : "odd", writeLine: lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        var rows = lines.Where(l => l.StartsWith("| ", StringComparison.Ordinal)).Skip(1).Select(l => l.Split('|'))
            .Select(c => (Leaf: c[1][1] == '\u00A0', Count: int.Parse(c[2], NumberStyles.Number))).ToList();
        await Assert.That(rows.Count(r => !r.Leaf)).IsEqualTo(11);
        var total = 0;
        for (int i = 0; i < rows.Count;)
        {
            var parent = rows[i++].Count;
            var leaves = 0;
            while (i < rows.Count && rows[i].Leaf) leaves += rows[i++].Count;
            await Assert.That(leaves).IsEqualTo(parent);
            total += parent;
        }
        await Assert.That(total).IsEqualTo(ran);
    }

    /// <summary>The names are only built for a failure, by generating the operations again, so they must match the values that ran.</summary>
    [Test]
    public async Task SampleModelBased_Failure_Names_The_Operations_That_Ran()
    {
        var message = Assert.Throws<CsCheckException>(() => Gen.Const(() => (new List<int>(), new List<int>()))
            .SampleModelBased(
                Gen.Int[0, 9].Operation<List<int>, List<int>>((a, i) => a.Add(i), (m, i) => m.Add(i)),
                Gen.Int[10, 99].Operation<List<int>, List<int>>(i => "Big " + i, (a, i) => a.Add(i), (m, i) => m.Add(i)),
                equal: (a, _) => a.Distinct().Count() < 3 || !a.Exists(i => i >= 10), printActual: a => string.Join(",", a)))!.Message;
        TUnitX.WriteLine(message);
        await Assert.That(MessageLine(message, "Operations: ")).IsEqualTo(ExpectedNames(MessageLine(message, "Final Actual: ")));
    }

    [Test]
    public async Task SampleModelBasedAsync_Failure_Names_The_Operations_That_Ran()
    {
        var message = (await Assert.ThrowsAsync<CsCheckException>(() => Gen.Const(() => Task.FromResult((new List<int>(), new List<int>())))
            .SampleModelBasedAsync(
                Gen.Int[0, 9].Operation<List<int>, List<int>>((a, i) => { a.Add(i); return Task.CompletedTask; }, (m, i) => { m.Add(i); return Task.CompletedTask; }),
                Gen.Int[10, 99].Operation<List<int>, List<int>>(i => "Big " + i, (a, i) => { a.Add(i); return Task.CompletedTask; }, (m, i) => { m.Add(i); return Task.CompletedTask; }),
                equal: (a, _) => a.Distinct().Count() < 3 || !a.Exists(i => i >= 10), printActual: a => string.Join(",", a))))!.Message;
        TUnitX.WriteLine(message);
        await Assert.That(MessageLine(message, "Operations: ")).IsEqualTo(ExpectedNames(MessageLine(message, "Final Actual: ")));
    }

    static string MessageLine(string message, string label) => message.Split('\n').Single(l => l.Contains(label)).Split(label)[1];

    static string ExpectedNames(string actual) => Check.Print(actual.Split(',').Select(int.Parse).Select(i => i < 10 ? "Op0 " + i : "Big " + i).ToList());

    /// <summary>Classifier indents nested rows with U+00A0 non breaking spaces, which is the character in the literal
    /// below, so matching on an ordinary space finds nothing. It also keeps "empty" off the "non-empty" row.</summary>
    static string Leaf(string label) => "\u00A0" + label;

    /// <summary>The same for the async path, where the table has to be written after the returned task completes
    /// rather than before it is handed back.</summary>
    [Test]
    public async Task SampleModelBasedAsync_Classify()
    {
        var lines = new List<string>();
        await Gen.Int[0, 5].List[0, 3].Select(l => Task.FromResult((new ConcurrentBag<int>(l), l)))
        .SampleModelBasedAsync(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>(async (bag, i) => { await Task.Yield(); bag.Add(i); }, async (list, i) => { await Task.Yield(); list.Add(i); }),
            Gen.Operation<ConcurrentBag<int>, List<int>>(async bag => { await Task.Yield(); bag.TryTake(out _); }, async list => { await Task.Yield(); if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count, threads: 1,
            classify: list => list.Count == 0 ? "empty" : "non-empty", writeLine: lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        await Assert.That(lines.Any(l => l.Contains("| Op1"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains(Leaf("empty")))).IsTrue();
    }

    /// <summary>The table is written when the sample fails, which is when it is worth reading: it says what the walk was
    /// exploring at the point something broke. The classify overloads printed after the sample returned rather than in a
    /// finally, so the one run whose distribution you actually wanted was the one that discarded it. Covers all three
    /// shapes - the plain sample, the model based one, and the async one that prints after awaiting the task.</summary>
    [Test]
    public async Task Classify_Table_Survives_A_Failure()
    {
        var plain = new List<string>();
        Assert.Throws<CsCheckException>(() => Gen.Int[0, 9].Sample(
            i => i == 9 ? throw new CsCheckException("boom") : i < 5 ? "low" : "high",
            writeLine: plain.Add, iter: 1_000, threads: 1));
        foreach (var line in plain) TUnitX.WriteLine(line);
        // Not Leaf: nothing to nest under here, so the row is not indented the way the model based table's rows are.
        await Assert.That(plain.Any(l => l.Contains("| low"))).IsTrue();

        var modelBased = new List<string>();
        Assert.Throws<CsCheckException>(() => Gen.Int[0, 5].List[1, 3].Select(l => (new ConcurrentBag<int>(l), l))
            .SampleModelBased(
                Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
                // Disagrees on the model side only, so the equal check fails and the sample throws after shrinking.
                equal: (bag, list) => bag.Count == list.Count && list.Count < 2, threads: 1,
                classify: list => list.Count == 0 ? "empty" : "non-empty", writeLine: modelBased.Add));
        foreach (var line in modelBased) TUnitX.WriteLine(line);
        await Assert.That(modelBased.Any(l => l.Contains("| Op0"))).IsTrue();

        var async = new List<string>();
        await Assert.ThrowsAsync<CsCheckException>(async () => await Gen.Int[0, 9].SampleAsync(
            async i => { await Task.Yield(); return i == 9 ? throw new CsCheckException("boom") : i < 5 ? "low" : "high"; },
            writeLine: async.Add, iter: 1_000, threads: 1));
        foreach (var line in async) TUnitX.WriteLine(line);
        await Assert.That(async.Any(l => l.Contains("| low"))).IsTrue();
    }

    /// <summary>The table is written from a finally, so a throwing sink must neither fail a passing sample nor replace a failure.</summary>
    [Test]
    public void Classify_Table_Throwing_WriteLine()
    {
        static void Throw(string _) => throw new InvalidOperationException("the sink is gone");
        Gen.Int[0, 9].Sample(i => i < 5 ? "low" : "high", Throw, iter: 100);
        var initial = Gen.Int[0, 5].List.Select(l => (new List<int>(l), l));
        var add = Gen.Int[0, 5].Operation<List<int>, List<int>>((a, i) => a.Add(i), (m, i) => m.Add(i));
        initial.SampleModelBased(add, classify: m => m.Count == 0 ? "empty" : "non-empty", writeLine: Throw);
        Assert.Throws<CsCheckException>(() => initial.SampleModelBased(add, equal: (_, m) => m.Count < 3,
            classify: m => m.Count == 0 ? "empty" : "non-empty", writeLine: Throw));
    }

    /// <summary>Counts are long, so a table past int.MaxValue prints rather than overflowing the total.</summary>
    [Test]
    public async Task Classifier_Counts_Past_Int_MaxValue()
    {
        var classifier = new Classifier();
        classifier.AddCount("Op0/a", 3_000_000_000);
        classifier.AddCount("Op0/b", 2_000_000_000);
        classifier.AddCount("Op0/b", 1_000_000_000);
        classifier.AddCount("Op1/a", 1);
        var lines = new List<string>();
        classifier.Print(lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        await Assert.That(lines.Count(l => l.Contains(6_000_000_000L.ToString("#,##0")) && l.Contains("100.00%"))).IsEqualTo(1);
        await Assert.That(lines.Count(l => l.Contains(3_000_000_000L.ToString("#,##0")) && l.Contains("50.00%"))).IsEqualTo(2);
    }

    /// <summary>Without a classify, writeLine only gets the iteration count, as it did before classify existed.</summary>
    [Test]
    public async Task SampleModelBased_WriteLine_Without_Classify_Writes_No_Table()
    {
        var lines = new List<string>();
        Gen.Int[0, 5].List.Select(l => (new ConcurrentBag<int>(l), l))
        .SampleModelBased(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
            Gen.Operation<ConcurrentBag<int>, List<int>>(bag => bag.TryTake(out _), list => { if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count, threads: 1, writeLine: lines.Add);
        await Gen.Int[0, 5].List.Select(l => Task.FromResult((new ConcurrentBag<int>(l), l)))
        .SampleModelBasedAsync(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>(async (bag, i) => { await Task.Yield(); bag.Add(i); }, async (list, i) => { await Task.Yield(); list.Add(i); }),
            Gen.Operation<ConcurrentBag<int>, List<int>>(async bag => { await Task.Yield(); bag.TryTake(out _); }, async list => { await Task.Yield(); if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count, threads: 1, writeLine: lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines.All(l => l.StartsWith("Passed ", StringComparison.Ordinal))).IsTrue();
    }

    [Test, Skip("failing")]
    public void SampleParallel_ConcurrentDictionary()
    {
        Gen.Dictionary(Gen.Int[0, 100], Gen.Byte)[0, 10].Select(l => new ConcurrentDictionary<int, byte>(l))
        .SampleParallel(
            Gen.Int[0, 100].Select(Gen.Byte)
            .Operation<ConcurrentDictionary<int, byte>>(t =>$"d[{t.Item1}] = {t.Item2}", (d, t) => d[t.Item1] = t.Item2),

            Gen.Int[0, 100]
            .Operation<ConcurrentDictionary<int, byte>>(i => $"TryRemove({i})", (d, i) => d.TryRemove(i, out _))
        );
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Faster_Repeat_Below_One_Is_Rejected(int repeat)
    {
        static void Same() { }
        var message = Assert.Throws<CsCheckException>(
            () => Check.Faster(Same, Same, repeat: repeat, timeout: 1))!.Message;
        await Assert.That(message).Contains("repeat must be at least 1");
    }

    /// <summary>Rejected at construction, so it holds for a run that never renders a report.</summary>
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task FasterAsync_Repeat_Below_One_Is_Rejected(int repeat)
    {
        static Task Quick() => Task.CompletedTask;
        static Task Slow() { for (int i = 0; i < 400; i++) _ = i * i; return Task.CompletedTask; }
        var message = (await Assert.ThrowsAsync<CsCheckException>(
            () => Check.FasterAsync(Quick, Slow, repeat: repeat, timeout: 1)))!.Message;
        await Assert.That(message).Contains("repeat must be at least 1");
    }

    [Test]
    public void FasterResult_Ties_Are_Never_Enough_To_Conclude()
    {
        Gen.Int[1, 50].Sample(ties =>
        {
            var result = new Check.FasterResult(6.0, 1, false);
            for (int i = 0; i < ties; i++)
                if (result.Add(100, 100)) return false;
            return true;
        });
    }

    [Test]
    public void SampleParallel_Works_With_One_Thread_Available()
    {
        Gen.Const(() => new ConcurrentQueue<int>())
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _)),
            iter: 20, threads: 1);
    }

    [Test]
    public void SampleParallel_ConcurrentQueue()
    {
        Gen.Const(() => new ConcurrentQueue<int>())
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _))
        );
    }

    /// <summary>The one thread clamp applies to the model overload too, not just the single state one.</summary>
    [Test]
    public void SampleParallelModel_Works_With_One_Thread_Available()
    {
        Gen.Const(() => (new ConcurrentQueue<int>(), new Queue<int>()))
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>, Queue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i), (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>, Queue<int>>("TryDequeue()", q => q.TryDequeue(out _), q => q.TryDequeue(out _)),
            iter: 20, threads: 1);
    }

    [Test]
    public void SampleParallelModel_ConcurrentQueue()
    {
        Gen.Const(() => (new ConcurrentQueue<int>(), new Queue<int>()))
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>, Queue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i), (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>, Queue<int>>("TryDequeue()", q => q.TryDequeue(out _), q => q.TryDequeue(out _))
        );
    }

    [Test]
    public void SampleParallelModel_ConcurrentStack()
    {
        Gen.Const(() => (new ConcurrentStack<int>(), new Stack<int>()))
        .SampleParallel(
            Gen.Int.Operation<ConcurrentStack<int>, Stack<int>>(i => $"Push({i})", (q, i) => q.Push(i), (q, i) => q.Push(i)),
            Gen.Operation<ConcurrentStack<int>, Stack<int>>("TryPop()", q => q.TryPop(out _), q => q.TryPop(out _))
        );
    }

    [Test]
    public void SampleParallelModel_ConcurrentDictionary()
    {
        Gen.Const(() => (new ConcurrentDictionary<int, int>(), new Dictionary<int, int>()))
        .SampleParallel(
            Gen.Int[1, 5].Operation<ConcurrentDictionary<int, int>, Dictionary<int, int>>(i => $"Set ({i})", (q, i) => q[i] = i, (q, i) => q[i] = i),
            Gen.Int[1, 5].Operation<ConcurrentDictionary<int, int>, Dictionary<int, int>>(i => $"TryRemove ({i})", (q, i) => q.TryRemove(i, out _), (q, i) => q.Remove(i))
        );
    }

    sealed class ParallelCounter { public int Count; }

    [Test]
    public async Task Linearizable_Skips_Sequences_That_Throw()
    {
        (string, Action<ParallelCounter>) assertPositive = ("AssertPositive", c => { if (c.Count == 0) throw new InvalidOperationException("zero"); });
        (string, Action<ParallelCounter>) inc = ("Inc", c => c.Count++);
        var linearizable = Check.Linearizable([], [assertPositive, assertPositive, assertPositive, assertPositive, assertPositive, inc, inc, inc, inc, inc],
            [0, 0, 0, 0, 0, 1, 1, 1, 1, 1], () => new ParallelCounter(), c => c.Count == 5);
        await Assert.That(linearizable).IsTrue();
    }

    [Test]
    public void SampleParallel_Seed_Replay_Reports_A_Later_Failing_Pass()
    {
        var equalCalls = 0;
        Assert.Throws<CsCheckException>(() => Gen.Const(() => new ParallelCounter())
            .SampleParallel(Gen.Operation<ParallelCounter>("Inc", c => Interlocked.Increment(ref c.Count)),
                equal: (_, _) => Interlocked.Increment(ref equalCalls) == 1, seed: "0002tXP34JM1", maxSequentialOperations: 0, iter: 1, replay: 2));
    }

    [Test]
    public async Task SampleParallel_Seed_Replay_Starts_Each_Pass_From_The_Initial_State()
    {
        var actualCounts = new ConcurrentBag<int>();
        Gen.Const(() => new ParallelCounter())
        .SampleParallel(Gen.Operation<ParallelCounter>("Inc", c => Interlocked.Increment(ref c.Count)),
            equal: (actual, replay) => { actualCounts.Add(actual.Count); return actual.Count == replay.Count; },
            seed: "0002tXP34JM1", maxSequentialOperations: 0, iter: 1, replay: 3);
        await Assert.That(actualCounts.Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task SampleParallelModel_Seed_Replay_Starts_Each_Pass_From_The_Initial_State()
    {
        var actualCounts = new ConcurrentBag<int>();
        Gen.Const(() => (new ParallelCounter(), new ParallelCounter()))
        .SampleParallel(Gen.Operation<ParallelCounter, ParallelCounter>("Inc", a => Interlocked.Increment(ref a.Count), m => m.Count++),
            equal: (actual, model) => { actualCounts.Add(actual.Count); return actual.Count == model.Count; },
            seed: "0002tXP34JM1", maxSequentialOperations: 0, iter: 1, replay: 3);
        await Assert.That(actualCounts.Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public void MedianEstimator_Minimum_And_Maximum_Are_Exact()
    {
        Gen.Int[1, 40].SelectMany(n => Gen.Int[-50, 50].Select(i => (double)i).Array[n])
        .Sample(a =>
        {
            var estimator = new MedianEstimator();
            foreach (var d in a) estimator.Add(d);
            return estimator.Minimum == a.Min() && estimator.Maximum == a.Max();
        });
    }

    [Test]
    [Arguments(3.0, 1.3499e-3)]
    [Arguments(4.0, 3.1671e-5)]
    [Arguments(5.0, 2.8665e-7)]
    [Arguments(6.0, 9.8659e-10)]
    public void ChiSquared_False_Failures_Are_Within_A_Factor_Of_The_Normal_Rate(double sigma, double normalRate)
    {
        Gen.OneOf(Gen.Int[10, 60].Array[2], Gen.Int[10, 30].Array[3])
        .Sample(expected => FalseFailureRate(expected) < 10 * normalRate, iter: 20);

        double FalseFailureRate(int[] expected)
        {
            var n = expected.Sum();
            var lnFactorial = new double[n + 1];
            for (int i = 1; i <= n; i++) lnFactorial[i] = lnFactorial[i - 1] + Math.Log(i);
            var actual = new int[expected.Length];
            return Rate(0, n, lnFactorial[n]);

            double Rate(int bucket, int remaining, double lnProbability)
            {
                if (bucket == expected.Length - 1)
                {
                    actual[bucket] = remaining;
                    try
                    {
                        Check.ChiSquared(expected, actual, sigma);
                        return 0;
                    }
                    catch (CsCheckException)
                    {
                        return Math.Exp(lnProbability + remaining * Math.Log((double)expected[bucket] / n) - lnFactorial[remaining]);
                    }
                }
                var rate = 0.0;
                for (int count = 0; count <= remaining; count++)
                {
                    actual[bucket] = count;
                    rate += Rate(bucket + 1, remaining - count, lnProbability + count * Math.Log((double)expected[bucket] / n) - lnFactorial[count]);
                }
                return rate;
            }
        }
    }

    [Test]
    public void ChiSquared_Fails_A_Generator_That_Misses_Buckets()
    {
        Assert.Throws<CsCheckException>(() => Check.ChiSquared([100, 100], [200, 0]));
        Assert.Throws<CsCheckException>(() => Check.ChiSquared([.. Enumerable.Repeat(10, 70)], [.. Enumerable.Repeat(20, 35), .. Enumerable.Repeat(0, 35)]));
    }

    [Test]
    public void ChiSquared_Passes_A_Perfect_Fit()
    {
        foreach (var buckets in new[] { 2, 10, 100, 1000 })
            Check.ChiSquared([.. Enumerable.Repeat(10, buckets)], [.. Enumerable.Repeat(10, buckets)]);
    }

    [Test]
    public async Task ChiSquared_One_Bucket_Is_Rejected()
    {
        var message = Assert.Throws<CsCheckException>(() => Check.ChiSquared([10], [10]))!.Message;
        await Assert.That(message).Contains("2 buckets");
    }

    [Test]
    public void Enqueue_Faster_Than_Median()
    {
        Gen.Double.OneTwo.Array[10].Select(Gen.Double.OneTwo, (a, s) =>
        {
            var median = new MedianEstimator();
            foreach (var d in a) median.Add(d);
            var queue = new Queue<double>(100);
            return (median, queue, s);
        })
        .Faster(
            (_, q, s) => q.Enqueue(s),
            (m, _, s) => m.Add(s),
            repeat: 100,
            writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void Equality_Int()
    {
        Check.Equality(Gen.Int);
    }

    [Test]
    public void Equality_Double()
    {
        Check.Equality(Gen.Double);
    }

    [Test]
    public void Equality_String()
    {
        Check.Equality(Gen.String);
    }

    sealed record Account(int Id, string Note)
    {
        public bool Equals(Account? other) => other is not null && Id == other.Id;
        public override int GetHashCode() => Id.GetHashCode();
    }

    static Gen<Account> GenAccount => Gen.Select(Gen.Int, Gen.String, (id, note) => new Account(id, note));

    [Test]
    public void Equality_Fields()
    {
        GenAccount.Equality(f => f
            .Compared((a, v) => a with { Id = v }, Gen.Int)
            .Ignored((a, v) => a with { Note = v }, Gen.String));
    }


    [Test]
    public void Equality_Fields_Detects_Ignored_Declared_As_Compared()
    {
        Assert.Throws<CsCheckException>(() => GenAccount.Equality(f => f
            .Compared((a, v) => a with { Id = v }, Gen.Int)
            .Compared((a, v) => a with { Note = v }, Gen.String)));
    }

    [Test]
    public void Equality_Fields_Detects_Compared_Declared_As_Ignored()
    {
        Assert.Throws<CsCheckException>(() => GenAccount.Equality(f => f
            .Ignored((a, v) => a with { Id = v }, Gen.Int)
            .Ignored((a, v) => a with { Note = v }, Gen.String)));
    }

    [Test]
    public void Equality_Fields_Detects_Missing_Field()
    {
        Assert.Throws<CsCheckException>(() => GenAccount.Equality(f => f
            .Ignored((a, v) => a with { Note = v }, Gen.String)));
    }

    [Test]
    public void Equality_Fields_Detects_Non_Varying_Gen()
    {
        Assert.Throws<CsCheckException>(() => GenAccount.Equality(f => f
            .Compared((a, v) => a with { Id = v }, Gen.Const(0))));
    }

    [Test]
    public void Equality_Fields_Mutable()
    {
        Gen.Select(Gen.Int, Gen.String, (id, note) => new MutableAccount(id, note))
        .Equality(f => f
            .Compared((a, v) => a.Id = v, Gen.Int)
            .Ignored((a, v) => a.Note = v, Gen.String));
    }

    [Test]
    public void Equality_Fields_Nested()
    {
        Gen.Select(Gen.String, Gen.Int, Gen.String, (name, house, street) => new Person(name, new Address(house, street)))
        .Equality(f => f
            .Compared((p, v) => p with { Name = v }, Gen.String)
            .Compared((p, v) => p with { Addr = p.Addr with { House = v } }, Gen.Int)
            .Ignored((p, v) => p with { Addr = p.Addr with { Street = v } }, Gen.String));
    }

    [Test]
    public void Equality_Fields_Comparer()
    {
        GenAccount.Equality(new AccountNoteComparer(), f => f
            .Compared((a, v) => a with { Note = v }, Gen.String)
            .Ignored((a, v) => a with { Id = v }, Gen.Int));
    }

    [Test]
    public void Equality_Fields_Normalized()
    {
        Gen.Int[0, 1000].Select(x => new Rounded(x)).Equality(f => f
            .Compared((r, v) => r with { Raw = v }, Gen.Int[0, 1000], new RoundToTenComparer()));
    }

    [Test]
    public async Task Equality_DistinctPair_Skips_Under_Shrinking_But_Throws_At_Root()
    {
        var pair = new GenDistinctPair<int>(Gen.Const(0), EqualityComparer<int>.Default, "field");
        Assert.Throws<CsCheckException>(() => pair.Generate(PCG.Parse("0000000000aa"), null, out _));
        var min = new Size(0);
        pair.Generate(PCG.Parse("0000000000aa"), min, out var size);
        await Assert.That(Size.IsLessThan(size, min)).IsFalse();
    }

    abstract record Either
    {
        public sealed record L(string Name, int Version) : Either
        {
            public bool Equals(L? other) => other is not null && Name == other.Name; // Version ignored
            public override int GetHashCode() => Name.GetHashCode();
        }
        public sealed record R(int X, int Y) : Either;
    }

    static Gen<Either> GenEither =>
        Gen.OneOf<Either>(
            Gen.Select(Gen.String, Gen.Int, (n, v) => new Either.L(n, v)),
            Gen.Select(Gen.Int, Gen.Int, (x, y) => new Either.R(x, y)));

    static EqualityFields<Either> DeclareEither(EqualityFields<Either> f) => f
        .Case<Either.L>(af => af
            .Compared((l, s) => l with { Name = s }, Gen.String)
            .Ignored((l, i) => l with { Version = i }, Gen.Int))
        .Case<Either.R>(rf => rf
            .Compared((r, x) => r with { X = x }, Gen.Int)
            .Compared((r, y) => r with { Y = y }, Gen.Int));

    [Test]
    public void Equality_Fields_Either()
    {
        GenEither.Equality(DeclareEither);
    }

    [Test]
    public void Equality_Fields_Either_Detects_Ignored_Declared_As_Compared()
    {
        Assert.Throws<CsCheckException>(() => GenEither.Equality(f => f
            .Case<Either.L>(af => af
                .Compared((l, s) => l with { Name = s }, Gen.String)
                .Compared((l, i) => l with { Version = i }, Gen.Int)) // Version is actually ignored
            .Case<Either.R>(rf => rf
                .Compared((r, x) => r with { X = x }, Gen.Int)
                .Compared((r, y) => r with { Y = y }, Gen.Int))));
    }

    [Test]
    public void Equality_Fields_Either_Detects_Missing_Field_In_Arm()
    {
        Assert.Throws<CsCheckException>(() => GenEither.Equality(f => f
            .Case<Either.L>(af => af
                .Compared((l, s) => l with { Name = s }, Gen.String)
                .Ignored((l, i) => l with { Version = i }, Gen.Int))
            .Case<Either.R>(rf => rf
                .Compared((r, x) => r with { X = x }, Gen.Int)))); // Y compared field omitted
    }

    [Test]
    public void Equality_Fields_Either_Detects_Conflated_Cases()
    {
        Assert.Throws<CsCheckException>(() => GenEither.Equality(new AlwaysEqualEitherComparer(), DeclareEither));
    }

    abstract record Nested
    {
        public sealed record Inner(Either Value) : Nested;
        public sealed record Outer(int X, int Y) : Nested;
    }

    static Gen<Nested> GenNested =>
        Gen.OneOf<Nested>(
            GenEither.Select(e => new Nested.Inner(e)),
            Gen.Select(Gen.Int, Gen.Int, (x, y) => new Nested.Outer(x, y)));

    [Test]
    public void Equality_Fields_Nested_Either()
    {
        GenNested.Equality(f => f
            .Case<Nested.Inner>(inf => inf
                .Union(n => n.Value, (_, e) => new Nested.Inner(e))
                    .Case<Either.L>(lf => lf
                        .Compared((l, s) => l with { Name = s }, Gen.String)
                        .Ignored((l, i) => l with { Version = i }, Gen.Int))
                    .Case<Either.R>(rf => rf
                        .Compared((r, x) => r with { X = x }, Gen.Int)
                        .Compared((r, y) => r with { Y = y }, Gen.Int)))
            .Case<Nested.Outer>(of => of
                .Compared((o, x) => o with { X = x }, Gen.Int)
                .Compared((o, y) => o with { Y = y }, Gen.Int)));
    }

    sealed class AlwaysEqualEitherComparer : IEqualityComparer<Either>
    {
        public bool Equals(Either? a, Either? b) => true;
        public int GetHashCode(Either e) => 0;
    }

    // A record with a sum-typed (subtype-sum) field. Tag is an ordinary compared field; the Either field is reached
    // with the fluent Union(down, up) builder, whose Case is compile-time constrained to real arms of Either.
    sealed record Holder(int Tag, Either Choice);

    [Test]
    public void Equality_Fields_Union_Field()
    {
        Gen.Select(Gen.Int, GenEither, (t, e) => new Holder(t, e))
        .Equality(f => f
            .Compared((h, v) => h with { Tag = v }, Gen.Int)
            .Union(h => h.Choice, (h, c) => h with { Choice = c })
                .Case<Either.L>(lf => lf
                    .Compared((l, s) => l with { Name = s }, Gen.String)
                    .Ignored((l, i) => l with { Version = i }, Gen.Int))
                .Case<Either.R>(rf => rf
                    .Compared((r, x) => r with { X = x }, Gen.Int)
                    .Compared((r, y) => r with { Y = y }, Gen.Int)));
    }



#if NET11_0_OR_GREATER
    sealed record Cat(string Name, int Whiskers)
    {
        public bool Equals(Cat? other) => other is not null && Name == other.Name; // Whiskers ignored
        public override int GetHashCode() => Name.GetHashCode();
    }

    sealed record Dog(string Name, string Breed);

    readonly union Pet(Cat, Dog);

    [Test]
    public void Equality_Fields_Union()
    {
        Gen.OneOf(
            Gen.Select(Gen.String, Gen.Int, (name, whiskers) => new Pet(new Cat(name, whiskers))),
            Gen.Select(Gen.String, Gen.String, (name, breed) => new Pet(new Dog(name, breed))))
        .Equality(f => f
            .Case<Cat>(cf => cf
                .Compared((c, s) => c with { Name = s }, Gen.String)
                .Ignored((c, w) => c with { Whiskers = w }, Gen.Int))
            .Case<Dog>(df => df
                .Compared((d, s) => d with { Name = s }, Gen.String)
                .Compared((d, b) => d with { Breed = b }, Gen.String)));
    }

    // Two similar-but-different arms, each with two compared fields and one ignored field (Timestamp).
    sealed record Sensor(string Id, double Reading, long Timestamp)
    {
        public bool Equals(Sensor? other) => other is not null && Id == other.Id && Reading == other.Reading; // Timestamp ignored
        public override int GetHashCode() => HashCode.Combine(Id, Reading);
    }

    sealed record Gauge(string Id, double Value, long Timestamp)
    {
        public bool Equals(Gauge? other) => other is not null && Id == other.Id && Value == other.Value; // Timestamp ignored
        public override int GetHashCode() => HashCode.Combine(Id, Value);
    }

    readonly union Signal(Sensor, Gauge);

    static Gen<Signal> GenSignal =>
        Gen.OneOf(
            Gen.Select(Gen.String, Gen.Double.Unit, Gen.Long, (id, r, t) => new Signal(new Sensor(id, r, t))),
            Gen.Select(Gen.String, Gen.Double.Unit, Gen.Long, (id, v, t) => new Signal(new Gauge(id, v, t))));

    [Test]
    public void Equality_Fields_Union_Both_Arms_Mixed()
    {
        GenSignal.Equality(f => f
            .Case<Sensor>(sf => sf
                .Compared((x, id) => x with { Id = id }, Gen.String)
                .Compared((x, r) => x with { Reading = r }, Gen.Double.Unit)
                .Ignored((x, t) => x with { Timestamp = t }, Gen.Long))
            .Case<Gauge>(gf => gf
                .Compared((x, id) => x with { Id = id }, Gen.String)
                .Compared((x, v) => x with { Value = v }, Gen.Double.Unit)
                .Ignored((x, t) => x with { Timestamp = t }, Gen.Long)));
    }

    sealed record Tagged(string Tag, Signal Signal);

    static Gen<Tagged> GenTagged =>
        Gen.Select(Gen.String, GenSignal, (t, s) => new Tagged(t, s));

    [Test]
    public void Equality_Fields_Record_With_Union_Field()
    {
        GenTagged.Equality(f => f
            .Compared((o, s) => o with { Tag = s }, Gen.String)
            .Union(o => o.Signal, (o, sig) => o with { Signal = sig }, sf => sf
                .Case<Sensor>(ssf => ssf
                    .Compared((x, id) => x with { Id = id }, Gen.String)
                    .Compared((x, r) => x with { Reading = r }, Gen.Double.Unit)
                    .Ignored((x, t) => x with { Timestamp = t }, Gen.Long))
                .Case<Gauge>(gf => gf
                    .Compared((x, id) => x with { Id = id }, Gen.String)
                    .Compared((x, v) => x with { Value = v }, Gen.Double.Unit)
                    .Ignored((x, t) => x with { Timestamp = t }, Gen.Long))));
    }
#endif

    sealed class MutableAccount(int id, string note)
    {
        public int Id = id;
        public string Note = note;
        public override bool Equals(object? obj) => obj is MutableAccount m && m.Id == Id;
        public override int GetHashCode() => Id.GetHashCode();
    }

    sealed record Address(int House, string Street);

    sealed record Person(string Name, Address Addr)
    {
        public bool Equals(Person? other) => other is not null && Name == other.Name && Addr.House == other.Addr.House;
        public override int GetHashCode() => HashCode.Combine(Name, Addr.House);
    }

    sealed record Rounded(int Raw)
    {
        int Bucket => (Raw + 5) / 10 * 10;
        public bool Equals(Rounded? other) => other is not null && Bucket == other.Bucket;
        public override int GetHashCode() => Bucket.GetHashCode();
    }

    sealed class AccountNoteComparer : IEqualityComparer<Account>
    {
        public bool Equals(Account? a, Account? b) => a is null ? b is null : b is not null && a.Note == b.Note;
        public int GetHashCode(Account a) => a.Note.GetHashCode();
    }

    sealed class RoundToTenComparer : IEqualityComparer<int>
    {
        static int Round(int v) => (v + 5) / 10 * 10;
        public bool Equals(int a, int b) => Round(a) == Round(b);
        public int GetHashCode(int v) => Round(v).GetHashCode();
    }
}

#if NET11_0_OR_GREATER
// Builds the member -> union conversion (a public constructor `T(TArm)`) once per (T, TArm) pair.
static class UnionCtor<T, TArm> where T : System.Runtime.CompilerServices.IUnion
{
    public static readonly Func<TArm, T> Up = Build();

    static Func<TArm, T> Build()
    {
        var p = System.Linq.Expressions.Expression.Parameter(typeof(TArm), "a");
        var ctor = typeof(T).GetConstructor([typeof(TArm)])
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no constructor taking a {typeof(TArm).Name}.");
        return System.Linq.Expressions.Expression.Lambda<Func<TArm, T>>(
            System.Linq.Expressions.Expression.New(ctor, p), p).Compile();
    }
}

// A builder that scopes case declarations for a C# union type T. Because T is fixed on the builder, Case needs only
// the arm type argument, and the predicate, down- and up-projections are all derived (IUnion.Value + the constructor).
sealed class UnionFields<T> where T : System.Runtime.CompilerServices.IUnion
{
    readonly EqualityFields<T> fields;
    internal UnionFields(EqualityFields<T> fields) => this.fields = fields;

    public UnionFields<T> Case<TArm>(Func<EqualityFields<TArm>, EqualityFields<TArm>> armFields)
    {
        fields.Case(t => t.Value is TArm, t => (TArm)t.Value!, (_, a) => UnionCtor<T, TArm>.Up(a), armFields);
        return this;
    }

    public static implicit operator EqualityFields<T>(UnionFields<T> u) => u.fields;
}

static class UnionEqualityFields
{
    // Top-level union: hand the fields callback a UnionFields<T> so arms read as f.Case<Sensor>(...) with no .Union().
    public static void Equality<T>(this Gen<T> gen, Func<UnionFields<T>, EqualityFields<T>> fields,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T, T), string>? print = null)
        where T : System.Runtime.CompilerServices.IUnion
        => gen.Equality((EqualityFields<T> f) => fields(new UnionFields<T>(f)), seed, iter, time, threads, print);

    // Union-typed field: hand the callback a UnionFields<TField> so arms read as sf.Case<Sensor>(...) with no inner .Union().
    public static EqualityFields<TParent> Union<TParent, TField>(this EqualityFields<TParent> fields,
        Func<TParent, TField> down, Func<TParent, TField, TParent> up, Func<UnionFields<TField>, EqualityFields<TField>> fieldFields)
        where TField : System.Runtime.CompilerServices.IUnion
        => fields.Union(down, up, sf => fieldFields(new UnionFields<TField>(sf)));
}
#endif