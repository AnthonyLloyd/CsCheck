using System;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class GenTests
    {
        int[] ArrayRepeat(int x, int n)
        {
            var a = new int[n];
            while (--n >= 0) a[n] = x;
            return a;
        }

        int[] Tally(int n, int[] ia)
        {
            var a = new int[n];
            for (int i = 0; i < ia.Length; i++) a[ia[i]]++;
            return a;
        }

        [Fact]
        public void Bool_Distribution()
        {
            var frequency = 10;
            var expected = ArrayRepeat(frequency, 2);
            Gen.Bool.Select(i => i ? 1 : 0).Array(frequency * 2)
            .Select(i => Tally(2, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void SByte_Range()
        {
            (from t in Gen.Select(Gen.SByte, Gen.SByte)
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.SByte[0, (sbyte)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Byte[0, (byte)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Short[0, (short)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.UShort[0, (ushort)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Int[0, buckets - 1]
            .Select(i => i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.UInt[0, (uint)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Long[0, buckets - 1]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.ULong[0, (ulong)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Single_Unit_Range()
        {
            Gen.Single.Unit.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
        }

        [Fact]
        public void Single_Range()
        {
            (from t in Gen.Single.Unit.Select(Gen.Single.Unit)
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Single.Unit
            .Select(i => (int)(i * buckets)).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Double_Unit_Range()
        {
            Gen.Double.Unit.Sample(f => Assert.InRange(f, 0.0, 0.99999999999999978));
        }

        [Fact]
        public void Double_Range()
        {
            (from t in Gen.Double.Unit.Select(Gen.Double.Unit)
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Double.Unit
            .Select(i => (int)(i * buckets)).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Decimal_Unit_Range()
        {
            Gen.Decimal.Unit.Sample(i => Assert.InRange(i, 0.0M, 0.99999999999999978M));
        }

        [Fact]
        public void Decimal_Range()
        {
            (from t in Gen.Decimal.Unit.Select(Gen.Decimal.Unit)
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Decimal.Unit
            .Select(i => (int)(i * buckets)).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            var expected = ArrayRepeat(frequency, buckets);
            Gen.Char[(char)0, (char)(buckets - 1)]
            .Select(i => (int)i).Array(frequency * buckets)
            .Select(i => Tally(buckets, i))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
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
            (from f in Gen.Select(Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[1, 5])
             let expected = new[] { f.V0 * frequency, f.V1 * frequency, f.V2 * frequency }
             from actual in Gen.Frequency((f.V0, 0), (f.V1, 1), (f.V2, 2)).Array(frequency * (f.V0 + f.V1 + f.V2))
                            .Select(i => Tally(3, i))
             select (expected, actual))
            .SampleOne(t => Check.ChiSquared(t.expected, t.actual));
        }
    }
}