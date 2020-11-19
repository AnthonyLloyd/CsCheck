using System;
using System.Collections.Generic;
using ImTools.Experimental;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class IMToolsTests
    {
        readonly Action<string> writeLine;
        public IMToolsTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        [Fact]
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

        [Fact]
        public void AddOrUpdate_random_items_and_randomly_checking_CsCheck()
        {
            const int upperBound = 11966;
            Gen.Int[0, upperBound].Array[1, 12].Sample(ints =>
            {
                var m = ImHashMap234<int, int>.Empty;
                foreach(var n in ints)
                {
                    m = m.AddOrUpdate(n, n);
                    Assert.Equal(n, m.GetValueOrDefault(n));
                }
                Assert.Equal(0, m.GetValueOrDefault(upperBound + 1));
                Assert.Equal(0, m.GetValueOrDefault(-1));
            }, size: 1_000_000_000/*, seed: "2Tt3UJ9PI4Hs3"*/);
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


        [Fact]
        public void AddOrUpdate_random_items_and_randomly_checking_metamorphic()
        {
            const int upperBound = 100000;
            Gen.Select(GenMap(upperBound), Gen.Int[0, upperBound], Gen.Int, Gen.Int[0, upperBound], Gen.Int)
            .Sample(t =>
            {
                var (m, k1, v1, k2, v2) = t;
                var m1 = m.AddOrUpdate(k1, v1).AddOrUpdate(k2, v2);
                var m2 = k1 == k2 ? m.AddOrUpdate(k2, v2) : m.AddOrUpdate(k2, v2).AddOrUpdate(k1, v1);
                var s1 = m1.Enumerate().OrderBy(i => i.Key);
                var s2 = m2.Enumerate().OrderBy(i => i.Key);
                Assert.Equal(s1.Select(i => i.Key), s2.Select(i => i.Key));
                Assert.Equal(s1.Select(i => i.Value), s2.Select(i => i.Value));
            }, size: 1_000_000);
        }
    }
}
