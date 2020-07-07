using System;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class GenTests
    {
        [Fact]
        public void Bool_Distribution()
        {
            var frequency = 10;
            var expected = new int[2];
            for (int i = 0; i < 2; i++) expected[i] = frequency;
            var actual = new int[2];
            var sample = Gen.Bool.AsEnumerable().Take(frequency * 2);
            foreach (var i in sample) actual[i ? 1 : 0]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Int_Range()
        {
            (from t in Gen.Int.Tuple(Gen.Int)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Int[start, finish]
             select (value, start, finish))
            .Assert(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Int_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Int[0, buckets - 1].AsEnumerable().Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Long_Range()
        {
            (from t in Gen.Long.Tuple(Gen.Long)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Long[start, finish]
             select (value, start, finish))
            .Assert(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Long_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Long[0, buckets - 1].AsEnumerable().Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Float_Default_Range()
        {
            Gen.Float.Assert(f => Assert.InRange(f, -100f, 100f));
        }

        [Fact]
        public void Float_Range()
        {
            (from t in Gen.Float.Tuple(Gen.Float)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Float[start, finish]
             select (value, start, finish))
            .Assert(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Float_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Float.AsEnumerable().Take(frequency * buckets);
            foreach (var i in sample) actual[(int)((i / 200f + 0.5f) * buckets)]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Double_Default_Range()
        {
            Gen.Double.Assert(f => Assert.InRange(f, -100.0, 100.0));
        }

        [Fact]
        public void Double_Range()
        {
            (from t in Gen.Double.Tuple(Gen.Double)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Double[start, finish]
             select (value, start, finish))
            .Assert(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Double_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Double.AsEnumerable().Take(frequency * buckets);
            foreach (var i in sample) actual[(int)((i / 200.0 + 0.5) * buckets)]++;
            Check.ChiSquared(expected, actual);
        }
    }
}