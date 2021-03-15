using System;
using System.Linq;
using System.Collections.Generic;
using CsCheck;
using Xunit;

namespace Tests
{
    public class ShrinkingChallendgeTests
    {
        //[Fact]
        public void No1_Bound5() // ([-32768], [-1], [], [], [])
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
            }
            , size: 100);
        }

        //[Fact]
        public void No2_LargeUnionList() // [[0, 1, -1, 2, -2]]
        {
            Gen.Int.Array.Array
            .Sample(aa =>
            {
                var hs = new HashSet<int>();
                foreach(var a in aa)
                {
                    foreach (var i in a) hs.Add(i);
                    if (hs.Count >= 5) return false;
                }
                return true;
            }, size: 100);
        }

        //[Fact]
        public void No3_Reverse() // [0, 1] or [1, 0]
        {
            Gen.Int.Array
            .Sample(a =>
            {
                var r = (int[])a.Clone();
                Array.Reverse(r);
                return Check.Equal(a, r);
            }, size: 100);
        }

        //[Fact]
        public void No4_Calculator() // 1 / (3 + -3)
        {

        }

        //[Fact]
        public void No5_LengthList() // [900]
        {
            Gen.Int[0, 1000].Array[1, 100]
            .Sample(a =>
            {
                foreach (var i in a)
                    if (i >= 900) return false;
                return true;
            }, size: 100_000);
        }

        //[Fact]
        public void No6_Difference_MustNotBeZero() // [10, 10]
        {
            var positive = Gen.Int[1, int.MaxValue];
            positive.Select(positive).Sample(t => t.V0 < 10 || t.V0 != t.V1);
        }

        //[Fact]
        public void No6_Difference_MustNotBeSmall() // [10, 6]
        {
            var positive = Gen.Int[1, int.MaxValue];
            positive.Select(positive).Sample(t => t.V0 < 10 || Math.Abs(t.V0 - t.V1) > 4 || t.V0 == t.V1);
        }

        //[Fact]
        public void No6_Difference_MustNotBeOne() // [10, 9]
        {
            var positive = Gen.Int[1, int.MaxValue];
            positive.Select(positive).Sample(t => t.V0 < 10 || Math.Abs(t.V0 - t.V1) != 1);
        }

        //[Fact]
        public void No7_BinHeap() // (0, None, (0, (0, None, None), (1, None, None)))
        {

        }

        //[Fact]
        public void No8_Coupling() // [1, 0]
        {
            Gen.Int[0, 100].SelectMany(l => Gen.Int[0, l - 1].Array[l])
            .Sample(a => {
                for (int i = 0; i < a.Length; i++)
                {
                    int j = a[i];
                    if (i != j && a[j] == i) return false;
                }
                return true;
            }, size: 100_000);
        }

        //[Fact]
        public void No9_Deletion() // ([0, 0], 0)
        {
            Gen.Int.List[1, 100].Select(l => Gen.Int[0, l.Count - 1])
            .Sample(t =>
            {
                var l = new List<int>(t.V0);
                var x = l[t.V1];
                l.Remove(x);
                return !l.Contains(x);
            });
        }

        //[Fact]
        public void No10_Distinct() // [0, 1, -1] or [0, 1, 2]
        {
            Gen.Int.Array.Sample(a => a.ToHashSet().Count < 3);
        }

        //[Fact]
        public void No11_NestedLists() // [[0, 0, 0, 0, 0, 0, 0, 0, 0, 0]]
        {
            Gen.Int.Array.Array
            .Sample(aa => {
                int l = 0;
                foreach (var a in aa)
                    l += a.Length;
                return l <= 10;
            }, size: 10_000_000);
        }
    }
}