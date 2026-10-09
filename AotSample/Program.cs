using System.Collections.Concurrent;
using CsCheck;

Gen.Int.Array[0, 10].Sample(a => a.Length <= 10, writeLine: Console.WriteLine);
Gen.Int.List.Select(l => l.Count).Sample(c => c >= 0, writeLine: Console.WriteLine);
Gen.String.HashSet.Sample(h => h.Count != int.MinValue, writeLine: Console.WriteLine);
Gen.Double.Array2D.Sample(a => a.Rank == 2, writeLine: Console.WriteLine);
Gen.Int.ArrayUnique.Sample(a => a.Distinct().Count() == a.Length, writeLine: Console.WriteLine);
Gen.Int.Array[0, 2].List[0, 2].HashSet[0, 2].Array[0, 2].List[0, 2].HashSet[0, 2].Sample(h => h.Count <= 2, writeLine: Console.WriteLine, iter: 10);
Gen.Select(Gen.Int, Gen.Double, Gen.String).Sample((_, _, s) => s is not null, writeLine: Console.WriteLine);
Gen.Decimal.Sample(d => d >= decimal.MinValue, writeLine: Console.WriteLine);
Gen.Int.Array.Select(a => (new List<int>(a), new List<int>(a)))
    .SampleModelBased(Gen.Int.Operation<List<int>, List<int>>((l, i) => l.Add(i), (l, i) => l.Add(i)), writeLine: Console.WriteLine);
Gen.Const(() => new ConcurrentQueue<int>())
    .SampleParallel(
        Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
        Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _)),
        writeLine: Console.WriteLine);
Spec.From(0)
    .Action("Inc", i => i < 5, i => i + 1)
    .Terminal(i => i == 5)
    .Never("NO-SIX", "the counter never reaches six", (_, a) => a == 6)
    .Exhaustive(Console.WriteLine);
Console.WriteLine(Check.Print(Gen.Int.Array[1, 2].List[1, 2].Array[1, 2].HashSet[1, 2].Generate(new PCG(1, 1), null, out _)));
Check.Faster(() => Enumerable.Range(0, 100).Sum(), () => Enumerable.Range(0, 100).Sum(i => i * 2 / 2), threads: 1, timeout: 5, raiseexception: false, writeLine: Console.WriteLine);
