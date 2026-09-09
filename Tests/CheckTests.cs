namespace Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public async Task ModelEqual_HashSet()
    {
        await Assert.That(Check.ModelEqual(
            new HashSet<int> { 1, 2, 3, 4 },
            new List<int> { 4, 3, 2, 1 }
        )).IsTrue();
    }

    [Test]
    public async Task ModelEqual_List()
    {
#pragma warning disable CA1861 // Avoid constant arrays as arguments
        await Assert.That(Check.ModelEqual(
            new List<int> { 1, 2, 3, 4 },
            new int[] { 1, 2, 3, 4 }
        )).IsTrue();
        await Assert.That(Check.ModelEqual(
            new List<int> { 1, 2, 3, 4 },
            new int[] { 1, 2, 4, 3 }
        )).IsFalse();
#pragma warning restore CA1861 // Avoid constant arrays as arguments
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
    }

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

    /// <summary>Without a classify the table is still written, one row per operation, which is the cheap signal that a
    /// random walk has starved an operation. Nothing is written at all when writeLine is left unset, so the default
    /// path pays nothing.</summary>
    [Test]
    public async Task SampleModelBased_Operation_Counts()
    {
        var lines = new List<string>();
        Gen.Int[0, 5].List.Select(l => (new ConcurrentBag<int>(l), l))
        .SampleModelBased(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
            Gen.Operation<ConcurrentBag<int>, List<int>>(bag => bag.TryTake(out _), list => { if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count, threads: 1, writeLine: lines.Add);
        foreach (var line in lines) TUnitX.WriteLine(line);
        await Assert.That(lines.Any(l => l.Contains("| Op0"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("| Op1"))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains("empty"))).IsFalse();
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
    public void SampleParallel_ConcurrentQueue()
    {
        Gen.Const(() => new ConcurrentQueue<int>())
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _))
        );
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
