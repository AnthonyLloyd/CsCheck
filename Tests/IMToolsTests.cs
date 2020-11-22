using System;
using System.Collections.Generic;
using ImTools.Experimental;
using Xunit;
using CsCheck;
using System.Linq;
using ImTools;

namespace Tests
{
    public class IMToolsTests
    {
        readonly Action<string> writeLine;
        public IMToolsTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_random_items_and_randomly_checking()
        {
            const int upperBound = 100000;
            var savedSeed = new Random().Next(0, upperBound);
            var rnd = new Random(savedSeed);

            var m = ImHashMap234<int, int>.Empty;
            for (var i = 0; i < 5000; i++)
            {
                var n = rnd.Next(0, upperBound);
                m = m.AddOrUpdate(n, n);
                Assert.Equal(n, m.GetValueOrDefault(n));
            }

            // non-existing keys 
            Assert.Equal(0, m.GetValueOrDefault(upperBound + 1));
            Assert.Equal(0, m.GetValueOrDefault(-1));
        }

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_random_items_and_randomly_checking_CsCheck()
        {
            const int upperBound = 11966;
            Gen.Int[0, upperBound].Array[1, 12].Sample(ints =>
            {
                var m = ImHashMap234<int, int>.Empty;
                foreach (var n in ints)
                {
                    m = m.AddOrUpdate(n, n);
                    Assert.Equal(n, m.GetValueOrDefault(n));
                }
                Assert.Equal(0, m.GetValueOrDefault(upperBound + 1));
                Assert.Equal(0, m.GetValueOrDefault(-1));
            }, size: 1_000/*, seed: "2Tt3UJ9PI4Hs3"*/);
        }

        static Gen<ImHashMap234<int, int>> GenMap(int upperBound) =>
            Gen.Int[0, upperBound].ArrayUnique.SelectMany(ks =>
                Gen.Int.Array[ks.Length].Select(vs =>
                {
                    var m = ImHashMap234<int, int>.Empty;
                    for (int i = 0; i < ks.Length; i++)
                        m = m.AddOrUpdate(ks[i], vs[i]);
                    return m;
                }));

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_Metamorphic()
        {
            const int upperBound = 100000;
            Gen.Select(GenMap(upperBound), Gen.Int[0, upperBound], Gen.Int, Gen.Int[0, upperBound], Gen.Int)
            .Sample(t =>
            {
                var map1 = t.V0.AddOrUpdate(t.V1, t.V2).AddOrUpdate(t.V3, t.V4);
                var map2 = t.V1 == t.V3 ? t.V0.AddOrUpdate(t.V3, t.V4) : t.V0.AddOrUpdate(t.V3, t.V4).AddOrUpdate(t.V1, t.V2);
                var seq1 = map1.Enumerate().OrderBy(i => i.Key).Select(i => (i.Key, i.Value));
                var seq2 = map2.Enumerate().OrderBy(i => i.Key).Select(i => (i.Key, i.Value));
                Assert.Equal(seq1, seq2);
            }
            , size: 100_000
            , print: t => t + "\n" + string.Join("\n", t.V0.Enumerate())
            , seed: "42ChASl6qJI5");
        }

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_ModelBased()
        {
            const int upperBound = 100000;
            Gen.Select(GenMap(upperBound), Gen.Int[0, upperBound], Gen.Int)
            .Sample(t =>
            {
                var dic1 = new Dictionary<int, int>();
                foreach (var entry in t.V0.Enumerate()) dic1.Add(entry.Key, entry.Value);
                dic1[t.V1] = t.V2;
                var dic2 = new Dictionary<int, int>();
                foreach (var entry in t.V0.AddOrUpdate(t.V1, t.V2).Enumerate())
                    dic2.Add(entry.Key, entry.Value);
                Assert.Equal(dic1, dic2);
            }
            , size: 100_000
            , print: t => t + "\n" + string.Join("\n", t.V0.Enumerate()));
        }
    }
}