﻿using System;
using System.Collections.Generic;
using ImTools.Experimental;
using Xunit;
using CsCheck;
using System.Linq;
using ImTools;
using System.Globalization;

namespace Tests
{
    public class IMToolsTests
    {
        [Fact]
        public void ModelEqual_ImHashMap234()
        {
            Assert.True(Check.ModelEqual(
                ImHashMap234<int, int>.Empty.AddOrUpdate(1, 2).AddOrUpdate(3, 4)
                .Enumerate().Select(kv => ImTools.KeyValuePair.Pair(kv.Key, kv.Value)),
                new Dictionary<int, int> { {3, 4}, {1, 2 } }
            ));
        }


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
            }, iter: 1_000/*, seed: "2Tt3UJ9PI4Hs3"*/);
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

        class ImHolder<T> { public T Im; }

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_Metamorphic()
        {
            const int upperBound = 100000;
            GenMap(upperBound)
            .Select(i => new ImHolder<ImHashMap234<int, int>> { Im = i })
            .SampleMetamorphic(
                Gen.Select(Gen.Int[0, upperBound], Gen.Int, Gen.Int[0, upperBound], Gen.Int).Metamorphic<ImHolder<ImHashMap234<int, int>>>(
                    (d, t) => d.Im = d.Im.AddOrUpdate(t.V0, t.V1).AddOrUpdate(t.V2, t.V3),
                    (d, t) => d.Im = t.V0 == t.V2 ? d.Im.AddOrUpdate(t.V2, t.V3) : d.Im.AddOrUpdate(t.V2, t.V3).AddOrUpdate(t.V0, t.V1)
                )
                , equal: (a, b) => Check.Equal(a.Im.Enumerate().Select(j => (j.Key, j.Value)), b.Im.Enumerate().Select(j => (j.Key, j.Value)))
                , print: i => Check.Print(i.Im.Enumerate().Select(j => (j.Key, j.Value)))
            );
        }

        [Fact(Skip = "Experiment")]
        public void AddOrUpdate_ModelBased()
        {
            const int upperBound = 100000;
            GenMap(upperBound).Select(d =>
            {
                var m = new Dictionary<int, int>();
                foreach (var entry in d.Enumerate()) m[entry.Key] = entry.Value;
                return (new ImHolder<ImHashMap234<int, int>> { Im = d }, m);
            })
            .SampleModelBased(
                Gen.Int[0, upperBound].Select(Gen.Int).Operation<ImHolder<ImHashMap234<int, int>>, Dictionary<int, int>>((h, d, kv) =>
                {
                    h.Im = h.Im.AddOrUpdate(kv.V0, kv.V1);
                    d[kv.V0] = kv.V1;
                })
                , equal: (h, d) =>
                {
                    var he = h.Im.Enumerate().Select(kv => (kv.Key, kv.Value)).ToList();
                    return he.Count == d.Count && !he.Except(d.Select(kv => (kv.Key, kv.Value))).Any();
                }
                , printActual: h => Check.Print(h.Im.Enumerate().Select(kv => (kv.Key, kv.Value)))
                , iter: 100_000
            );
        }
    }
}