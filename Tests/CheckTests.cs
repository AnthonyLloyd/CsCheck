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
    public void Equality()
    {
        Check.Equality(Gen.Int);
        Check.Equality(Gen.Double);
        Check.Equality(Gen.String);
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
}