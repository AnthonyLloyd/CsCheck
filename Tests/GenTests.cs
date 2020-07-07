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
            var sample = Gen.Bool.Take(frequency * 2);
            foreach (var i in sample) actual[i ? 1 : 0]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void SByte_Range()
        {
            (from t in Gen.SByte.Tuple(Gen.SByte)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.SByte[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void SByte_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.SByte[0, (sbyte)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Byte_Range()
        {
            (from t in Gen.Byte.Tuple(Gen.Byte)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Byte[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Byte_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Byte[0, (byte)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Short_Range()
        {
            (from t in Gen.Short.Tuple(Gen.Short)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Short[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Short_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Short[0, (short)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void UShort_Range()
        {
            (from t in Gen.UShort.Tuple(Gen.UShort)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.UShort[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void UShort_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.UShort[0, (ushort)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
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
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Int_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Int[0, buckets - 1].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void UInt_Range()
        {
            (from t in Gen.UInt.Tuple(Gen.UInt)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.UInt[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void UInt_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.UInt[0, (uint)(buckets - 1)].Take(frequency * buckets);
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
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Long_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Long[0, buckets - 1].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void ULong_Range()
        {
            (from t in Gen.ULong.Tuple(Gen.ULong)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.ULong[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void ULong_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.ULong[0, (ulong)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Single_Default_Range()
        {
            Gen.Single.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
        }

        [Fact]
        public void Single_Range()
        {
            (from t in Gen.Single.Tuple(Gen.Single)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Single[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Single_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Single.Take(frequency * buckets);
            foreach (var i in sample) actual[(int)(i * buckets)]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Double_Default_Range()
        {
            Gen.Double.Sample(f => Assert.InRange(f, 0.0, 0.99999999999999978));
        }

        [Fact]
        public void Double_Range()
        {
            (from t in Gen.Double.Tuple(Gen.Double)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Double[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Double_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Double.Take(frequency * buckets);
            foreach (var i in sample) actual[(int)(i * buckets)]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Decimal_Default_Range()
        {
            Gen.Decimal.Sample(i => Assert.InRange(i, 0.0M, 0.99999999999999978M));
        }

        [Fact]
        public void Decimal_Range()
        {
            (from t in Gen.Decimal.Tuple(Gen.Decimal)
             let start = Math.Min(t.Item1, t.Item2)
             let finish = Math.Max(t.Item1, t.Item2)
             from value in Gen.Decimal[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Decimal_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Decimal.Take(frequency * buckets);
            foreach (var i in sample) actual[(int)(i * buckets)]++;
            Check.ChiSquared(expected, actual);
        }

        [Fact]
        public void Char_Range()
        {
            (from t in Gen.Char.Tuple(Gen.Char)
             let start = t.Item1 > t.Item2 ? t.Item2 : t.Item1
             let finish = t.Item1 > t.Item2 ? t.Item1 : t.Item2
             from value in Gen.Char[start, finish]
             select (value, start, finish))
            .Sample(i => Assert.InRange(i.value, i.start, i.finish));
        }

        [Fact]
        public void Char_Distribution()
        {
            var frequency = 10;
            var buckets = 70;
            var expected = new int[buckets];
            for (int i = 0; i < buckets; i++) expected[i] = frequency;
            var actual = new int[buckets];
            var sample = Gen.Char[(char)0, (char)(buckets - 1)].Take(frequency * buckets);
            foreach (var i in sample) actual[i]++;
            Check.ChiSquared(expected, actual);
        }
    }
}