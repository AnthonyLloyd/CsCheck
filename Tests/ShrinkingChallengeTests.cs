namespace Tests;

using System;
using System.Linq;
using System.Collections.Generic;
using CsCheck;
using Xunit;

public class ShrinkingChallengeTests
{
    [Fact(Skip="fails")]
    public void No1_Bound5()
    {
        static short Sum(short[] l)
        {
            short s = 0;
            for (int i = 0; i < l.Length; i++) s += l[i];
            return s;
        }
        var sGen = Gen.Short.Array[0, 10].Where(i => Sum(i) < 256);
        Gen.Select(sGen, sGen, sGen, sGen, sGen)
        .Sample(t =>
        {
            var total = Sum(t.V0) + Sum(t.V1) + Sum(t.V2) + Sum(t.V3) + Sum(t.V4);
            return (short)total < 5 * 256;
        });
    }

    [Fact(Skip="fails")]
    public void No2_LargeUnionList()
    {
        Gen.Int.Array.Array
        .Sample(aa =>
        {
            var hs = new HashSet<int>();
            foreach (var a in aa)
            {
                foreach (var i in a) hs.Add(i);
                if (hs.Count >= 5) return false;
            }
            return true;
        });
    }

    [Fact(Skip="fails")]
    public void No3_Reverse()
    {
        Gen.Int.Array
        .Sample(a =>
        {
            var c = new int[a.Length];
            for (int i = 0; i < a.Length; i++)
                c[i] = a[i];
            Array.Reverse(c);
            return Check.Equal(a, c);
        });
    }

    [Fact(Skip="fails")]
    public void No4_Calculator()
    {
        static bool DivSubTerms(object o) => o switch
        {
            int i => true,
            ('/', object x, int y) => y != 0,
            (_, object x, object y) => DivSubTerms(x) && DivSubTerms(y),
            _ => throw new Exception(o.ToString())
        };

        static int Evaluate(object o) => o switch
        {
            int i => i,
            ('/', object x, object y) => Evaluate(x) / Evaluate(y),
            ('+', object x, object y) => Evaluate(x) + Evaluate(y),
            _ => throw new Exception(o.ToString())
        };

        Gen.Recursive<object>(g =>
            Gen.Frequency(
                (3, Gen.Int[-10, 10].Cast<object>()),
                (1, Gen.Select(Gen.Const('+'), g, g).Cast<object>()),
                (1, Gen.Select(Gen.Const('/'), g, g).Cast<object>())
            )
        )
        .Where(DivSubTerms)
        .Sample(o =>
        {
            int i = Evaluate(o);
        });
    }

    [Fact(Skip="fails")]
    public void No5_LengthList()
    {
        Gen.Int.Array
        .Sample(a =>
        {
            foreach (var i in a)
                if (i >= 900) return false;
            return true;
        });
    }

    [Fact(Skip="fails")]
    public void No6_Difference_MustNotBeZero()
    {
        Gen.Int.Positive.Select(Gen.Int.Positive)
        .Sample(t => t.V0 < 10 || t.V0 != t.V1);
    }

    [Fact(Skip="fails")]
    public void No6_Difference_MustNotBeSmall()
    {
        Gen.Int.Positive.Select(Gen.Int.Positive)
        .Sample(t => t.V0 < 10 || Math.Abs(t.V0 - t.V1) > 4 || t.V0 == t.V1);
    }

    [Fact(Skip="fails")]
    public void No6_Difference_MustNotBeOne()
    {
        Gen.Int.Positive.Select(Gen.Int.Positive)
        .Sample(t => t.V0 < 10 || Math.Abs(t.V0 - t.V1) != 1);
    }

    class Heap { public int Head; public Heap Left; public Heap Right; }

    [Fact(Skip="fails")]
    public void No7_BinHeap()
    {
        static uint Count(Heap h) => h is null ? 0 : 1 + Count(h.Left) + Count(h.Right);

        static Heap MergeHeaps(Heap h1, Heap h2) =>
            h1 is null ? h2
          : h2 is null ? h1
          : h1.Head <= h2.Head ? new Heap { Head = h1.Head, Left = MergeHeaps(h1.Right, h2), Right = h1.Left }
          : new Heap { Head = h2.Head, Left = MergeHeaps(h2.Right, h1), Right = h2.Left };

        static List<int> ToList(Heap heap)
        {
            var r = new List<int>();
            var s = new Stack<Heap>();
            s.Push(heap);
            while (s.Count != 0)
            {
                var h = s.Pop();
                if (h == null) continue;
                r.Add(h.Head);
                s.Push(h.Left);
                s.Push(h.Right);
            }
            return r;
        }

        static List<int> WrongToSortedList(Heap heap)
        {
            var r = new List<int>();
            if (heap is not null)
            {
                r.Add(heap.Head);
                r.AddRange(ToList(MergeHeaps(heap.Left, heap.Right)));
            }
            return r;
        }

        static List<int> Sorted(List<int> l)
        {
            l = new(l);
            l.Sort();
            return l;
        }

        static string Print(Heap h) => h is null ? "None" : $"({h.Head}, {Print(h.Left)}, {Print(h.Right)})";

        Gen.Recursive(g =>
            Gen.Frequency(
                (3, Gen.Const((Heap)null)),
                (1, Gen.Select(Gen.Int, g, g, (h, l, r) => new Heap { Head = h, Left = l, Right = r }))
            ),
            (Heap h, ref Size size) => { size = new Size(Count(h), size); return h; }
        )
        .Sample(h =>
        {
            var l1 = ToList(h);
            var l2 = WrongToSortedList(h);
            return Check.Equal(l2, Sorted(l2)) && Check.Equal(Sorted(l1), l2);
        }, print: Print);
    }

    [Fact(Skip="fails")]
    public void No8_Coupling()
    {
        Gen.Int[0, 100].SelectMany(l => Gen.Int[0, l - 1].Array[l])
        .Sample(a =>
        {
            for (int i = 0; i < a.Length; i++)
            {
                int j = a[i];
                if (i != j && a[j] == i) return false;
            }
            return true;
        });
    }

    [Fact(Skip="fails")]
    public void No9_Deletion()
    {
        Gen.Int.List[1, 100].SelectTuple(l => Gen.Int[0, l.Count - 1])
        .Sample(t =>
        {
            var l = new List<int>(t.V0);
            var x = l[t.V1];
            l.Remove(x);
            return !l.Contains(x);
        });
    }

    [Fact(Skip="fails")]
    public void No10_Distinct()
    {
        Gen.Int.Array
        .Sample(a => a.ToHashSet().Count < 3);
    }

    [Fact(Skip="fails")]
    public void No11_NestedLists()
    {
        Gen.Int.Array.Array
        .Sample(aa =>
        {
            int l = 0;
            foreach (var a in aa)
                l += a.Length;
            return l <= 10;
        });
    }
}