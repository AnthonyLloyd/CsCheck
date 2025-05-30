﻿namespace Tests;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

public class CheckTests(Xunit.Abstractions.ITestOutputHelper output)
{
    static void Assert_Commutative<T, R>(Gen<T> gen, Func<T, T, R> operation)
    {
        Gen.Select(gen, gen)
        .Sample((op1, op2) => Assert.Equal(operation(op1, op2), operation(op2, op1)));
    }

    [Fact]
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

    [Fact]
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
            Assert.Equal(operation(op1, operation(op2, op3)), operation(operation(op1, op2), op3)));
    }

    [Fact]
    public void Sample_Addition_Is_Associative()
    {
        Assert_Associative(Gen.UInt, (x, y) => x + y);
        Assert_Associative(Gen.Int, (x, y) => x + y);
        Assert_Associative(Gen.ULong, (x, y) => x + y);
        Assert_Associative(Gen.Long, (x, y) => x + y);
    }

    [Fact]
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

    [Fact]
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
            writeLine: output.WriteLine);
    }

    [Fact]
    public void Faster_Matrix_Multiply_Range()
    {
        var genDim = Gen.Int[5, 30];
        var genArray = Gen.Double.Unit.Array2D;
        Gen.SelectMany(genDim, genDim, genDim, (i, j, k) => Gen.Select(genArray[i, j], genArray[j, k]))
        .Faster(
            MulIKJ,
            MulIJK,
            writeLine: output.WriteLine);
    }

    [Fact]
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
            writeLine: output.WriteLine);
    }

    [Fact]
    public void Faster_CustomCriterion()
    {
        var successCriterion = (double output1, double output2) => output1 >= 0.7 * output2;

        Gen.Double[100, 1000]
            .Faster(
                d => d*0.8,
                d =>
                {
                    Thread.Sleep(1);
                    return d;
                },
                equal: successCriterion,
                writeLine: output.WriteLine);
    }

    [Fact]
    public void Equal_Dictionary()
    {
        Assert.True(Check.Equal(
            new Dictionary<int, byte> { { 1, 2 }, { 3, 4 } },
            new Dictionary<int, byte> { { 3, 4 }, { 1, 2 } }
        ));
    }

    [Fact]
    public void Equal_List()
    {
        Assert.True(Check.Equal<List<int>>([1, 2, 3, 4], [1, 2, 3, 4]));
        Assert.False(Check.Equal<List<int>>([1, 2, 3, 4], [1, 2, 4, 3]));
    }

    [Fact]
    public void Equal_Array()
    {
        Assert.True(Check.Equal<int[]>([1, 2, 3, 4], [1, 2, 3, 4]));
        Assert.False(Check.Equal<int[]>([1, 2, 3, 4], [1, 2, 4, 3]));
    }

    [Fact]
    public void Equal_Array2D()
    {
        Assert.True(Check.Equal(
            new int[,] { { 1, 2 }, { 3, 4 } },
            new int[,] { { 1, 2 }, { 3, 4 } }
        ));
        Assert.False(Check.Equal(
            new int[,] { { 1, 2 }, { 3, 4 } },
            new int[,] { { 1, 2 }, { 4, 3 } }
        ));
    }

    [Fact]
    public void ModelEqual_HashSet()
    {
        Assert.True(Check.ModelEqual(
            new HashSet<int> { 1, 2, 3, 4 },
            new List<int> { 4, 3, 2, 1 }
        ));
    }

    [Fact]
    public void ModelEqual_List()
    {
#pragma warning disable CA1861 // Avoid constant arrays as arguments
        Assert.True(Check.ModelEqual(
            new List<int> { 1, 2, 3, 4 },
            new int[] { 1, 2, 3, 4 }
        ));
        Assert.False(Check.ModelEqual(
            new List<int> { 1, 2, 3, 4 },
            new int[] { 1, 2, 4, 3 }
        ));
#pragma warning restore CA1861 // Avoid constant arrays as arguments
    }

    [Fact]
    public void SampleModelBased_ConcurrentBag()
    {
        Gen.Int[0, 5].List.Select(l => (new ConcurrentBag<int>(l), l))
        .SampleModelBased(
            Gen.Int.Operation<ConcurrentBag<int>, List<int>>((bag, i) => bag.Add(i), (list, i) => list.Add(i)),
            Gen.Operation<ConcurrentBag<int>, List<int>>(bag => bag.TryTake(out _), list => { if (list.Count > 0) list.RemoveAt(0); }),
            equal: (bag, list) => bag.Count == list.Count
        , threads: 1);
    }

    [Fact]
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

    [Fact]
    public void SampleParallel_ConcurrentQueue()
    {
        Gen.Const(() => new ConcurrentQueue<int>())
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _))
        );
    }

    [Fact]
    public void SampleParallelModel_ConcurrentQueue()
    {
        Gen.Const(() => (new ConcurrentQueue<int>(), new Queue<int>()))
        .SampleParallel(
            Gen.Int.Operation<ConcurrentQueue<int>, Queue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i), (q, i) => q.Enqueue(i)),
            Gen.Operation<ConcurrentQueue<int>, Queue<int>>("TryDequeue()", q => q.TryDequeue(out _), q => q.TryDequeue(out _))
        );
    }

    [Fact]
    public void SampleParallelModel_ConcurrentStack()
    {
        Gen.Const(() => (new ConcurrentStack<int>(), new Stack<int>()))
        .SampleParallel(
            Gen.Int.Operation<ConcurrentStack<int>, Stack<int>>(i => $"Push({i})", (q, i) => q.Push(i), (q, i) => q.Push(i)),
            Gen.Operation<ConcurrentStack<int>, Stack<int>>("TryPop()", q => q.TryPop(out _), q => q.TryPop(out _))
        );
    }

    [Fact]
    public void SampleParallelModel_ConcurrentDictionary()
    {
        Gen.Const(() => (new ConcurrentDictionary<int, int>(), new Dictionary<int, int>()))
        .SampleParallel(
            Gen.Int[1, 5].Operation<ConcurrentDictionary<int, int>, Dictionary<int, int>>(i => $"Set ({i})", (q, i) => q[i] = i, (q, i) => q[i] = i),
            Gen.Int[1, 5].Operation<ConcurrentDictionary<int, int>, Dictionary<int, int>>(i => $"TryRemove ({i})", (q, i) => q.TryRemove(i, out _), (q, i) => q.Remove(i))
        );
    }

    [Fact]
    public void Equality()
    {
        Check.Equality(Gen.Int);
        Check.Equality(Gen.Double);
        Check.Equality(Gen.String);
    }

    [Fact]
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
            (m, q, s) => q.Enqueue(s),
            (m, q, s) => m.Add(s),
            repeat: 100,
            writeLine: output.WriteLine);
    }
}