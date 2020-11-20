using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace Tests
{
    public class GenTests
    {
        public static int[] ArrayRepeat(int n, int x)
        {
            var a = new int[n];
            Array.Fill(a, x);
            return a;
        }

        static int[] Tally(int n, int[] ia)
        {
            var a = new int[n];
            for (int i = 0; i < ia.Length; i++) a[ia[i]]++;
            return a;
        }

        [Fact]
        public void Bool_Distribution()
        {
            var frequency = 10;
            var expected = ArrayRepeat(2, frequency);
            Gen.Bool.Select(i => i ? 1 : 0).Array[2 * frequency]
            .Select(sample => Tally(2, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void SByte_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.SByte[0, (sbyte)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Byte_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Byte[0, (byte)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Short_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Short[0, (short)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void UShort_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.UShort[0, (ushort)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Int_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            int[] expected = ArrayRepeat(buckets, frequency);
            Gen.Int[0, buckets - 1].Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Int_Skew()
        {
            (from t in Gen.Int.Select(Gen.Int, Gen.Double[-10.0, 10.0])
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Int.Skew[start, finish, t.V2]
             select (value, start, finish, t.V2))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Int_Zigzag_Roundtrip()
        {
            Gen.Int.Sample(i => GenInt.Unzigzag(GenInt.Zigzag(i)) == i);
        }

        [Fact]
        public void UInt_Range()
        {
            (from t in Gen.UInt.Select(Gen.UInt)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.UInt[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void UInt_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.UInt[0, (uint)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void UInt_Skew()
        {
            (from t in Gen.UInt.Select(Gen.UInt, Gen.Double[-10.0, 10.0])
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.UInt.Skew[start, finish, t.V2]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Long_Range()
        {
            (from t in Gen.Long.Select(Gen.Long)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Long[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Long_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Long[0, buckets - 1]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
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
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void ULong_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.ULong[0, (ulong)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Single()
        {
            Gen.Single.Sample(i => i <= float.PositiveInfinity || float.IsNaN(i));
        }

        [Fact]
        public void Single_Unit_Range()
        {
            Gen.Single.Unit.Sample(f => f >= 0f && f <= 0.9999999f);
        }

        [Fact]
        public void Single_Range()
        {
            (from t in Gen.Single.Unit.Select(Gen.Single.Unit)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Single[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Single_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Single.Unit
            .Select(i => (int)(i * buckets))
            .Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Single_Normal()
        {
            Gen.Single.Normal
            .Sample(f => !float.IsNaN(f) && !float.IsInfinity(f));
        }

        [Fact]
        public void Single_NormalNonNegative()
        {
            Gen.Single.NormalNonNegative
            .Sample(f => !float.IsNaN(f) && !float.IsInfinity(f) && f >= 0.0f);
        }

        [Fact]
        public void Double_Unit_Range()
        {
            Gen.Double.Unit.Sample(f => f >= 0.0 && f <= 0.99999999999999978);
        }

        [Fact]
        public void Double_Range()
        {
            (from t in Gen.Double.Unit.Select(Gen.Double.Unit)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Double[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Double_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Double.Unit
            .Select(i => (int)(i * buckets))
            .Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Double_Skew()
        {
            (from t in Gen.Double.Unit.Select(Gen.Double.Unit, Gen.Double[-10.0, 10.0])
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Double.Skew[start, finish, t.V2]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Double_Normal()
        {
            Gen.Double.Normal
            .Sample(d => !double.IsNaN(d) && !double.IsInfinity(d));
        }

        [Fact]
        public void Double_NormalNonNegative()
        {
            Gen.Double.NormalNonNegative
            .Sample(d => !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0.0);
        }

        [Fact]
        public void Decimal()
        {
            Gen.Decimal.Unit.Sample(i => i <= decimal.MaxValue);
        }

        [Fact]
        public void Decimal_Unit_Range()
        {
            Gen.Decimal.Unit.Sample(i => i >= 0.0M && i <= 0.99999999999999978M);
        }

        [Fact]
        public void Decimal_Range()
        {
            (from t in Gen.Decimal.Unit.Select(Gen.Decimal.Unit)
             let start = Math.Min(t.V0, t.V1)
             let finish = Math.Max(t.V0, t.V1)
             from value in Gen.Decimal[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Decimal_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Decimal.Unit
            .Select(i => (int)(i * buckets))
            .Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Date_Range()
        {
            (from t in Gen.Date.Select(Gen.Date)
             let start = t.V0 < t.V1 ? t.V0 : t.V1
             let finish = t.V0 < t.V1 ? t.V1 : t.V0
             from value in Gen.Date[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void DateTime_Range()
        {
            (from t in Gen.DateTime.Select(Gen.DateTime)
             let start = t.V0 < t.V1 ? t.V0 : t.V1
             let finish = t.V0 < t.V1 ? t.V1 : t.V0
             from value in Gen.DateTime[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void TimeSpan_Range()
        {
            (from t in Gen.TimeSpan.Select(Gen.TimeSpan)
             let start = t.V0 < t.V1 ? t.V0 : t.V1
             let finish = t.V0 < t.V1 ? t.V1 : t.V0
             from value in Gen.TimeSpan[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void DateTimeOffset()
        {
            Gen.DateTimeOffset.Sample(i => { });
        }

        [Fact]
        public void Guid()
        {
            Gen.Guid.Sample(i => { });
        }

        [Fact]
        public void Char_Range()
        {
            (from t in Gen.Char.Select(Gen.Char)
             let start = t.V0 > t.V1 ? t.V1 : t.V0
             let finish = t.V0 > t.V1 ? t.V0 : t.V1
             from value in Gen.Char[start, finish]
             select (value, start, finish))
            .Sample(i => i.value >= i.start && i.value <= i.finish);
        }

        [Fact]
        public void Char_Distribution()
        {
            var buckets = 70;
            var frequency = 10;
            var expected = ArrayRepeat(buckets, frequency);
            Gen.Char[(char)0, (char)(buckets - 1)]
            .Cast<int>().Array[frequency * buckets]
            .Select(sample => Tally(buckets, sample))
            .SampleOne(actual => Check.ChiSquared(expected, actual));
        }

        [Fact]
        public void Char_Array()
        {
            var chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
            Gen.Char[chars].Sample(c => chars.Contains(c));
        }

        [Fact]
        public void List()
        {
            Gen.UShort[1, 1000]
            .List[10, 100]
            .Sample(l => l.Count >= 10 && l.Count <= 100
                      && l.All(i => i >= 1 && i <= 1000));
        }

        [Fact]
        public void Enumerable()
        {
            Gen.UShort[1, 1000]
            .Enumerable[10, 100]
            .Sample(l => l.Count() >= 10 && l.Count() <= 100
                      && l.All(i => i >= 1 && i <= 1000));
        }

        [Fact]
        public void HashSet()
        {
            Gen.ULong[1, 1000]
            .HashSet[10, 100]
            .Sample(i => i.Count >= 10 && i.Count <= 100
                      && i.All(j => j >= 1 && j <= 1000));
        }

        [Fact]
        public void Dictionary()
        {
            Gen.Dictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
            .Sample(i => i.Count >= 10 && i.Count <= 100
                      && i.All(j => j.Key >= 1 && j.Key <= 1000));
        }

        [Fact]
        public void SortedDictionary()
        {
            Gen.SortedDictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
            .Sample(i => i.Count >= 10 && i.Count <= 100
                      && i.All(j => j.Key >= 1 && j.Key <= 1000));
        }

        [Fact]
        public void OneOf()
        {
            Gen.OneOf(0, 1, 2).Sample(i => i >= 0 && i <= 2);
        }

        [Fact]
        public void Frequency()
        {
            var frequency = 10;
            (from f in Gen.Select(Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[1, 5])
             let expected = new[] { f.V0 * frequency, f.V1 * frequency, f.V2 * frequency }
             from actual in Gen.Frequency((f.V0, 0), (f.V1, 1), (f.V2, 2))
                            .Array[frequency * (f.V0 + f.V1 + f.V2)]
                            .Select(sample => Tally(3, sample))
             select (expected, actual))
            .SampleOne(t => Check.ChiSquared(t.expected, t.actual));
        }
    }
}