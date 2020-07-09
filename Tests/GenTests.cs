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
            (from t in Gen.SByte.Select(Gen.SByte)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Byte.Select(Gen.Byte)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Short.Select(Gen.Short)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.UShort.Select(Gen.UShort)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Int.Select(Gen.Int)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.UInt.Select(Gen.UInt)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Long.Select(Gen.Long)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.ULong.Select(Gen.ULong)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Single.Select(Gen.Single)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Double.Select(Gen.Double)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Decimal.Select(Gen.Decimal)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
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
            (from t in Gen.Char.Select(Gen.Char)
             let start = t.V0 > t.V1 ? t.V1 : t.V0
             let finish = t.V0 > t.V1 ? t.V0 : t.V1
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

        [Fact]
        public void Char_Array()
        {
            var chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
            Gen.Char[chars].Sample(c => Assert.True(chars.Contains(c)));
        }

        [Fact]
        public void OneOf()
        {
            Gen.OneOf(0, 1, 2).Sample(i => Assert.InRange(i, 0, 2));
        }

        [Fact]
        public void Frequency()
        {
            var frequency = 10;
            (from f in Gen.Select(Gen.Int[1, 9], Gen.Int[1, 9], Gen.Int[1, 9])
             let expected = new[] { f.V0 * frequency, f.V1 * frequency, f.V2 * frequency }
             from sample in Gen.Frequency((f.V0, Gen.Const(0)), (f.V1, Gen.Const(1)), (f.V2, Gen.Const(2)))
                            .Array(frequency * (f.V0 + f.V1 + f.V2))
             let actual = new[] { sample.Count(i => i == 0), sample.Count(i => i == 1), sample.Count(i => i == 2) }
             select (expected, actual))
            .Sample(t => Check.ChiSquared(t.expected, t.actual), size: 1);
        }
    }
}