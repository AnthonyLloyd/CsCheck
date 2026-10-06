namespace Tests;

using System;
using System.Runtime.InteropServices;
using CsCheck;

public class GenTests
{
    sealed class SizedGen<T>(T value, ulong sizeI) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            size = new Size(sizeI);
            return value;
        }
    }

    static int[] Tally(int n, int[] ia)
    {
        var a = new int[n];
        for (int i = 0; i < ia.Length; i++) a[ia[i]]++;
        return a;
    }

    [Test]
    public void Bool_Distribution()
    {
        const int frequency = 100;
        var expected = new int[] { frequency, frequency};
        Gen.Bool.Select(i => i ? 1 : 0).Array[2 * frequency]
        .Select(sample => Tally(2, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void SByte_MinMax()
    {
        Gen.SByte[sbyte.MinValue, sbyte.MaxValue].Single();
    }

    [Test]
    public void SByte_Range()
    {
        (from t in Gen.Select(Gen.SByte, Gen.SByte)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.SByte[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void SByte_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.SByte[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Byte_MinMax()
    {
        Gen.Byte[byte.MinValue, byte.MaxValue].Single();
    }

    [Test]
    public void Byte_Range()
    {
        (from t in Gen.Byte.Select(Gen.Byte)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Byte[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Byte_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Byte[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Short_Zigzag_Roundtrip()
    {
        Gen.Short.Sample(i => GenShort.Unzigzag(GenShort.Zigzag(i)) == i);
    }

    [Test]
    public void Short_MinMax()
    {
        Gen.Short[short.MinValue, short.MaxValue].Single();
    }

    [Test]
    public void Short_Range()
    {
        (from t in Gen.Short.Select(Gen.Short)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Short[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Short_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Short[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void UShort_MinMax()
    {
        Gen.UShort[ushort.MinValue, ushort.MaxValue].Single();
    }

    [Test]
    public void UShort_Range()
    {
        (from t in Gen.UShort.Select(Gen.UShort)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.UShort[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void UShort_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.UShort[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Int_MinMax()
    {
        Gen.Int[int.MinValue, int.MaxValue].Single();
    }

    [Test]
    public void Int_Range()
    {
        (from t in Gen.Int.Select(Gen.Int)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Int[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Int_Positive()
    {
        Gen.Int.Positive.Sample(i => i > 0);
    }

    [Test]
    public async Task Int_Positive_Gen_Method()
    {
        static (int, ulong) Method(uint s, uint v)
        {
            int i = 1 << (int)s;
            i = (int)v & (i - 1) | i;
            var size = s << 27 | (ulong)i & 0x7FF_FFFFUL;
            return (i, size);
        }
        await Assert.That(Method(0U, uint.MaxValue)).IsEqualTo((1, 1UL));
        await Assert.That(Method(0U, 57686U)).IsEqualTo((1, 1UL));
        await Assert.That(Method(30U, uint.MaxValue)).IsEqualTo((int.MaxValue, 0xF7FF_FFFFUL));
    }

    [Test]
    public async Task Short_Gen_Method()
    {
        static (short, ulong) Method(uint s, uint v)
        {
            ushort i = (ushort)(1U << (int)s);
            i = (ushort)((v & (i - 1) | i) - 1);
            var size = s << 11 | i & 0x7FFUL;
            return ((short)-GenShort.Unzigzag(i), size);
        }
        await Assert.That(Method(0U, uint.MaxValue)).IsEqualTo(((short)0, 0UL));
        await Assert.That(Method(0U, 7686U)).IsEqualTo(((short)0, 0UL));
        await Assert.That(Method(1U, uint.MaxValue - 1)).IsEqualTo(((short)1, 0x801UL));
        await Assert.That(Method(1U, uint.MaxValue)).IsEqualTo(((short)-1, 0x802UL));
        await Assert.That(Method(15U, uint.MaxValue - 1)).IsEqualTo((short.MaxValue, 0x7FFDUL));
        await Assert.That(Method(15U, uint.MaxValue)).IsEqualTo(((short)-short.MaxValue, 0x7FFEUL));
    }

    [Test]
    public async Task Int_Gen_Method()
    {
        static (int, ulong) Method(uint s, uint v)
        {
            uint i = 1U << (int)s;
            i = (v & (i - 1U) | i) - 1U;
            var size = s << 27 | i & 0x7FF_FFFFUL;
            return (-GenInt.Unzigzag(i), size);
        }
        await Assert.That(Method(0U, uint.MaxValue)).IsEqualTo((0, 0UL));
        await Assert.That(Method(0U, 57686U)).IsEqualTo((0, 0UL));
        await Assert.That(Method(1U, uint.MaxValue - 1)).IsEqualTo((1, 0x800_0001UL));
        await Assert.That(Method(1U, uint.MaxValue)).IsEqualTo((-1, 0x800_0002UL));
        await Assert.That(Method(31U, uint.MaxValue - 1)).IsEqualTo((int.MaxValue, 0xFFFF_FFFDUL));
        await Assert.That(Method(31U, uint.MaxValue)).IsEqualTo((-int.MaxValue, 0xFFFF_FFFEUL));
    }

    [Test]
    public void Int_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Int[0, buckets - 1].Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Int_Zigzag_Roundtrip()
    {
        Gen.Int.Sample(i => GenInt.Unzigzag(GenInt.Zigzag(i)) == i);
    }

    [Test]
    public async Task Zigzag()
    {
        await Assert.That(GenInt.Unzigzag(0U)).IsEqualTo(0);
        await Assert.That(GenInt.Unzigzag(1U)).IsEqualTo(-1);
        await Assert.That(GenInt.Unzigzag(2U)).IsEqualTo(1);
        await Assert.That(GenInt.Unzigzag(0xFFFFFFFEU)).IsEqualTo(int.MaxValue);
        await Assert.That(GenInt.Unzigzag(0xFFFFFFFFU)).IsEqualTo(int.MinValue);
    }

    [Test]
    public void UInt_MinMax()
    {
        Gen.UInt[uint.MinValue, uint.MaxValue].Single();
    }

    [Test]
    public void UInt_Range()
    {
        (from t in Gen.UInt.Select(Gen.UInt)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.UInt[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void UInt_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.UInt[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Long_Zigzag_Roundtrip()
    {
        Gen.Long.Sample(i => GenLong.Unzigzag(GenLong.Zigzag(i)) == i);
    }

    [Test]
    public async Task Long_Gen_Method()
    {
        static (long, ulong) Method(uint s, ulong v)
        {
            ulong i = 1UL << (int)s;
            i = (v & (i - 1UL) | i) - 1UL;
            var size = (ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU;
            return (-GenLong.Unzigzag(i), size);
        }
        await Assert.That(Method(0, ulong.MaxValue)).IsEqualTo((0, 0UL));
        await Assert.That(Method(0, 57686)).IsEqualTo((0, 0UL));
        await Assert.That(Method(63U, ulong.MaxValue - 1)).IsEqualTo((long.MaxValue, 0xF_FFFF_FFFF_FFFDUL));
        await Assert.That(Method(63U, ulong.MaxValue)).IsEqualTo((-long.MaxValue, 0xF_FFFF_FFFF_FFFEUL));
    }

    [Test]
    public void Long_MinMax()
    {
        Gen.Long[long.MinValue, long.MaxValue].Single();
    }

    [Test]
    public void Long_Range()
    {
        (from t in Gen.Long.Select(Gen.Long)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Long[start, finish]
         select (value, start, finish))
        .Sample(i => i.start <= i.value && i.value <= i.finish);
    }

    [Test]
    public void Long_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Long[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void ULong_MinMax()
    {
        Gen.ULong[ulong.MinValue, ulong.MaxValue].Single();
    }

    [Test]
    public void ULong_Range()
    {
        (from t in Gen.ULong.Select(Gen.ULong)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.ULong[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void ULong_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.ULong[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Single()
    {
        Gen.Single.Sample(i => i <= float.PositiveInfinity || float.IsNaN(i));
    }

    [Test]
    public void Single_Unit_Range()
    {
        Gen.Single.Unit.Sample(f => f is >= 0f and <= 0.9999999f);
    }

    [Test]
    public void Single_MinMax()
    {
        Gen.Single[float.MinValue, float.MaxValue].Single();
    }

    [Test]
    public void Single_RangeLarge()
    {
        Gen.Single[10e14f, 10e15f].Single();
    }

    [Test]
    public void Single_Range()
    {
        (from t in Gen.Single.Unit.Select(Gen.Single.Unit)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Single[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Single_Range_Beyond_The_Unit_Interval()
    {
        var g = Gen.Frequency(
            (2, Gen.UInt.Uniform.Select(BitConverter.UInt32BitsToSingle).Where(float.IsFinite)),
            (1, Gen.Single),
            (1, Gen.Single.Unit),
            (1, Gen.OneOf(0f, float.Epsilon, float.MinValue, float.MaxValue)));
        (from t in Gen.Select(g, g)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Single[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Single_Range_Edges()
    {
        (float, float)[] ranges = [(0f, 0f), (0f, float.Epsilon), (float.MinValue, float.MaxValue), (-1e38f, 1e38f), (3e8f, float.MaxValue),
            (1f, float.MaxValue), (float.MaxValue, float.MaxValue), (float.MinValue, float.MinValue)];
        foreach (var (start, finish) in ranges)
            Gen.Single[start, finish].Sample(f => f >= start && f <= finish, iter: 1000);
    }

    [Test]
    public void Single_Zero()
    {
        Gen.Single.Array[1000].Sample(fs => fs.Contains(0f));
        Gen.Single[-1000f, 1000f].Array[1000].Sample(fs => fs.Contains(0f));
    }

    [Test]
    public void Single_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Single.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Double_Unit_Range()
    {
        Gen.Double.Unit.Sample(f => f is >= 0.0 and <= 0.99999999999999978);
    }

    [Test]
    public void Double_MinMax()
    {
        Gen.Double[double.MinValue, double.MaxValue].Single();
    }

    [Test]
    public void Double_RangeLarge()
    {
        Gen.Double[10e14, 10e15].Single();
    }

    [Test]
    public void Double_Range_Beyond_The_Unit_Interval()
    {
        var g = Gen.Frequency(
            (2, Gen.ULong.Uniform.Select(BitConverter.UInt64BitsToDouble).Where(double.IsFinite)),
            (1, Gen.Double),
            (1, Gen.Double.Unit),
            (1, Gen.OneOf(0.0, double.Epsilon, double.MinValue, double.MaxValue)));
        (from t in Gen.Select(g, g)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Double[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Double_Range()
    {
        (from t in Gen.Double.Unit.Select(Gen.Double.Unit)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Double[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish, seed: "89rtRQWk16go", iter: 1);
    }

    [Test]
    public void Double_Range_Edges()
    {
        (double, double)[] ranges = [(0.0, 0.0), (0.0, 1e-100), (0.0, double.Epsilon), (double.MinValue, double.MaxValue), (-1e308, 1e308), (3e8, double.MaxValue),
            (1.0, double.MaxValue), (double.MaxValue, double.MaxValue), (double.MinValue, double.MinValue)];
        foreach (var (start, finish) in ranges)
            Gen.Double[start, finish].Sample(d => d >= start && d <= finish, iter: 1000);
    }

    [Test]
    public void Double_Zero()
    {
        Gen.Double.Array[1000].Sample(ds => ds.Contains(0.0));
        Gen.Double[-1000.0, 1000.0].Array[1000].Sample(ds => ds.Contains(0.0));
    }

    [Test]
    public void Double_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Double.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Decimal()
    {
        Gen.Decimal.Sample(i => {
            var c = new DecimalConverter { D = i };
            return (c.flags & ~(DecimalConverter.SignMask | DecimalConverter.ScaleMask)) == 0 && (c.flags & DecimalConverter.ScaleMask) <= (28 << 16);
        });
    }

    [Test]
    public void Decimal_Unit_Range()
    {
        Gen.Decimal.Unit.Sample(i => i is >= 0.0M and <= 0.99999999999999978M);
    }

    [Test]
    public void Decimal_MinMax()
    {
        Gen.Decimal[decimal.MinValue, decimal.MaxValue].Single();
    }

    [Test]
    public void Decimal_RangeLarge()
    {
        Gen.Decimal[10e14M, 10e15M].Single();
    }

    [Test]
    public void Decimal_Range()
    {
        var end = Gen.Frequency(
            (2, Gen.Select(Gen.Int, Gen.Int, Gen.Int, Gen.Bool, Gen.Byte[0, 28]).Select((lo, mid, hi, isNegative, scale) => new decimal(lo, mid, hi, isNegative, scale))),
            (1, Gen.Decimal),
            (1, Gen.Decimal.Unit));
        (from t in end.Select(end)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Decimal[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void Decimal_Range_Edges()
    {
        (decimal, decimal)[] ranges = [(0M, 0M), (0M, 1M), (-100M, 0M), (0M, 0.00000000000000000000000002M), (2.0000000000000000001M, 3M),
            (2.0000000000000000001M, 2.0000000000000000002M), (decimal.MaxValue, decimal.MaxValue), (decimal.MinValue, decimal.MinValue)];
        foreach (var (start, finish) in ranges)
            Gen.Decimal[start, finish].Sample(d => d >= start && d <= finish, iter: 1000);
    }

    [Test]
    public void Decimal_Zero()
    {
        Gen.Decimal.Array[1000].Sample(ds => ds.Contains(0M));
        Gen.Decimal[-1000M, 1000M].Array[1000].Sample(ds => ds.Contains(0M));
    }

    [Test]
    public void Decimal_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Decimal.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Date_MinMax()
    {
        Gen.Date[DateTime.MinValue.Date, DateTime.MaxValue.Date].Single();
    }

    [Test]
    public void Date_Range()
    {
        (from t in Gen.Date.Select(Gen.Date)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.Date[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void DateOnly_MinMax()
    {
        Gen.DateOnly[DateOnly.MinValue, DateOnly.MaxValue].Single();
    }

    [Test]
    public void DateOnly_Range()
    {
        (from t in Gen.DateOnly.Select(Gen.DateOnly)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.DateOnly[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void DateTime_MinMax()
    {
        Gen.DateTime[DateTime.MinValue, DateTime.MaxValue].Single();
    }

    [Test]
    public void DateTime_Range()
    {
        (from t in Gen.DateTime.Select(Gen.DateTime)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.DateTime[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void TimeOnly_MinMax()
    {
        Gen.TimeOnly[TimeOnly.MinValue, TimeOnly.MaxValue].Single();
    }

    [Test]
    public void TimeOnly_Range()
    {
        (from t in Gen.TimeOnly.Select(Gen.TimeOnly)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.TimeOnly[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void TimeSpan_MinMax()
    {
        Gen.TimeSpan[TimeSpan.MinValue, TimeSpan.MaxValue].Single();
    }

    [Test]
    public void TimeSpan_Range()
    {
        (from t in Gen.TimeSpan.Select(Gen.TimeSpan)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.TimeSpan[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public void DateTimeOffset()
    {
        Gen.DateTimeOffset.Sample(_ => { });
    }

    [Test]
    public void Guid()
    {
        Gen.Guid.Sample(_ => { });
    }

    [Test]
    public void Char_MinMax()
    {
        Gen.Char[char.MinValue, char.MaxValue].Single();
    }

    [Test]
    public void Char_Range()
    {
        (from t in Gen.Char.Select(Gen.Char)
         let start = t.Item1 > t.Item2 ? t.Item2 : t.Item1
         let finish = t.Item1 > t.Item2 ? t.Item1 : t.Item2
         from value in Gen.Char[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Test]
    public async Task Char_Range_Rejects_Finish_Before_Start()
    {
        var message = Assert.Throws<CsCheckException>(() => _ = Gen.Char['z', 'a'])!.Message;
        await Assert.That(message).Contains("finish");
    }

    [Test]
    public async Task Char_Chars_Rejects_Empty_And_Null()
    {
        await Assert.That(Assert.Throws<CsCheckException>(() => _ = Gen.Char[""])!.Message).Contains("empty");
        await Assert.That(Assert.Throws<CsCheckException>(() => _ = Gen.Char[null!])!.Message).Contains("null");
        await Assert.That(Assert.Throws<CsCheckException>(() => _ = Gen.String[""])!.Message).Contains("empty");
    }

    [Test]
    public void Char_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = new int[buckets];
        Array.Fill(expected, frequency);
        Gen.Char[(char)0, (char)(buckets - 1)]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual), iter: 1, time: -2);
    }

    [Test]
    public void Char_Array()
    {
        const string chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
        Gen.Char[chars].Sample(chars.Contains);
    }

    [Test]
    public void List()
    {
        Gen.UShort[1, 1000]
        .List[10, 100]
        .Sample(l => l.Count >= 10 && l.Count <= 100
                  && l.All(i => i is >= 1 and <= 1000));
    }

    [Test]
    public async Task Select_Tuple_Skips_When_Final_Field_Is_Not_Smaller()
    {
        var min = new Size(0);
        var actual = Gen.Select(new SizedGen<string>("a", 0), new SizedGen<string>("b", 1))
            .Generate(PCG.Parse("0000000000aa"), min, out var size);
        await Assert.That(actual).IsEqualTo(default);
        await Assert.That(Size.IsLessThan(size, min)).IsFalse();
    }

    [Test]
    public async Task Select_Skips_When_Intermediate_Field_Is_Not_Smaller()
    {
        var min = new Size(0);
        var actual = Gen.Select(
            new SizedGen<int>(1, 0),
            new SizedGen<int>(2, 0),
            new SizedGen<int>(3, 1),
            new SizedGen<int>(4, 0),
            new SizedGen<int>(5, 0),
            static (_, _, _, _, _) => 123)
            .Generate(PCG.Parse("0000000000aa"), min, out var size);
        await Assert.That(actual).IsEqualTo(0);
        await Assert.That(Size.IsLessThan(size, min)).IsFalse();
    }

    /// <summary>A Select of 9 to 16 generators draws them in order and adds their sizes, the same as generating each in turn.</summary>
    [Test]
    public void Select_9_To_16_Matches_Generating_Each_In_Turn()
    {
        var g = Gen.Int;
        Gen.Select(Gen.UInt, Gen.ULong).Sample((stream, seed) =>
            InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15 }))
         && InTurn(stream, seed, Gen.Select(g, g, g, g, g, g, g, g, g, g, g, g, g, g, g, g, (v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16) => new[] { v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16 })));
    }

    static bool InTurn(uint stream, ulong seed, Gen<int[]> select)
    {
        var selectPcg = new PCG(stream, seed);
        var values = select.Generate(selectPcg, null, out var size);
        var pcg = new PCG(stream, seed);
        var total = new Size(0);
        foreach (var value in values)
        {
            if (Gen.Int.Generate(pcg, null, out var s) != value) return false;
            total.Add(s);
        }
        return size.I == total.I && selectPcg.State == pcg.State;
    }

    [Test]
    public async Task Select_16_Skips_When_Intermediate_Field_Is_Not_Smaller()
    {
        var min = new Size(0);
        var z = new SizedGen<int>(0, 0);
        var actual = Gen.Select(z, z, z, z, z, z, z, z, z, z, z, z, new SizedGen<int>(13, 1), z, z, z,
            static (_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _) => 123)
            .Generate(PCG.Parse("0000000000aa"), min, out var size);
        await Assert.That(actual).IsEqualTo(0);
        await Assert.That(Size.IsLessThan(size, min)).IsFalse();
    }
    [Test]
    public async Task SelectMany_Skips_When_Result_Is_Not_Smaller()
    {
        var min = new Size(0, new Size(0));
        var actual = new SizedGen<int>(1, 0)
            .SelectMany(_ => new SizedGen<string>("result", 1))
            .Generate(PCG.Parse("0000000000aa"), min, out var size);
        await Assert.That(actual).IsNull();
        await Assert.That(Size.IsLessThan(size, min)).IsFalse();
    }

    [Test]
    public void HashSet()
    {
        Gen.ULong[1, 1000]
        .HashSet[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j is >= 1 and <= 1000));
    }

    [Test]
    public async Task ArrayUnique_Skips_Under_Shrinking_But_Throws_At_Root()
    {
        var gen = Gen.Const(0).ArrayUnique[2];
        Assert.Throws<CsCheckException>(() => gen.Generate(PCG.Parse("0000000000aa"), null, out _));
        gen.Generate(PCG.Parse("0000000000aa"), new Size(2UL << 32, new Size(0)), out var size);
        await Assert.That(Size.IsLessThan(size, new Size(2UL << 32, new Size(0)))).IsFalse();
    }

    [Test]
    public async Task HashSet_Skips_Under_Shrinking_But_Throws_At_Root()
    {
        var gen = Gen.Const(0).HashSet[2];
        Assert.Throws<CsCheckException>(() => gen.Generate(PCG.Parse("0000000000aa"), null, out _));
        gen.Generate(PCG.Parse("0000000000aa"), new Size(2UL << 32, new Size(0)), out var size);
        await Assert.That(Size.IsLessThan(size, new Size(2UL << 32, new Size(0)))).IsFalse();
    }

    [Test]
    public void Dictionary()
    {
        Gen.Dictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j.Key is >= 1 and <= 1000));
    }

    [Test]
    public async Task Dictionary_Skips_Under_Shrinking_But_Throws_At_Root()
    {
        var gen = Gen.Dictionary(Gen.Const(0), Gen.Bool)[2];
        Assert.Throws<CsCheckException>(() => gen.Generate(PCG.Parse("0000000000aa"), null, out _));
        gen.Generate(PCG.Parse("0000000000aa"), new Size(2UL << 32, new Size(0)), out var size);
        await Assert.That(Size.IsLessThan(size, new Size(2UL << 32, new Size(0)))).IsFalse();
    }

    [Test]
    public void SortedDictionary()
    {
        Gen.SortedDictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j.Key is >= 1 and <= 1000));
    }

    [Test]
    public async Task SortedDictionary_Skips_Under_Shrinking_But_Throws_At_Root()
    {
        var gen = Gen.SortedDictionary(Gen.Const(0), Gen.Bool)[2];
        Assert.Throws<CsCheckException>(() => gen.Generate(PCG.Parse("0000000000aa"), null, out _));
        gen.Generate(PCG.Parse("0000000000aa"), new Size(2UL << 32, new Size(0)), out var size);
        await Assert.That(Size.IsLessThan(size, new Size(2UL << 32, new Size(0)))).IsFalse();
    }

    [Test]
    public void OneOf_Const()
    {
        Gen.OneOf(0, 1, 2).Sample(i => i is >= 0 and <= 2);
    }

    [Test]
    public void OneOf_IGen()
    {
        Gen.OneOf(Gen.Const(0), Gen.Const(1), Gen.Const(2)).Sample(i => i is >= 0 and <= 2);
    }

    [Test]
    public void Frequency()
    {
        const int frequency = 100;
        (from f in Gen.Select(Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[1, 5])
         let expected = new[] { f.Item1 * frequency, f.Item2 * frequency, f.Item3 * frequency }
         from actual in Gen.Frequency((f.Item1, 0), (f.Item2, 1), (f.Item3, 2))
                        .Array[frequency * (f.Item1 + f.Item2 + f.Item3)]
                        .Select(sample => Tally(3, sample))
         select (expected, actual))
        .Sample(t => Check.ChiSquared(t.expected, t.actual), iter: 1, time: -2);
    }

    [Test]
    public async Task Frequency_Total_Zero_Is_Rejected()
    {
        var constants = Assert.Throws<CsCheckException>(() => Gen.Frequency((0, "a"), (0, "b")))!.Message;
        await Assert.That(constants).Contains("zero");
        var gens = Assert.Throws<CsCheckException>(() => Gen.Frequency((0, Gen.Const("a")), (0, Gen.Const("b"))))!.Message;
        await Assert.That(gens).Contains("zero");
    }

    [Test]
    public async Task Frequency_Negative_Weight_Is_Rejected()
    {
        var constants = Assert.Throws<CsCheckException>(() => Gen.Frequency((-2, "a"), (3, "b")))!.Message;
        await Assert.That(constants).Contains("negative");
        var gens = Assert.Throws<CsCheckException>(() => Gen.Frequency((-2, Gen.Const("a")), (3, Gen.Const("b"))))!.Message;
        await Assert.That(gens).Contains("negative");
    }

    [Test]
    public async Task Frequency_Total_Over_UInt_MaxValue_Is_Rejected()
    {
        var constants = Assert.Throws<CsCheckException>(() => Gen.Frequency((int.MaxValue, "a"), (int.MaxValue, "b"), (2, "c")))!.Message;
        await Assert.That(constants).Contains("uint.MaxValue");
        var gens = Assert.Throws<CsCheckException>(() => Gen.Frequency((int.MaxValue, Gen.Const("a")), (int.MaxValue, Gen.Const("b")), (2, Gen.Const("c"))))!.Message;
        await Assert.That(gens).Contains("uint.MaxValue");
    }

    [Test]
    public void Frequency_Matches_Cumulative_Weight_Oracle()
    {
        Gen.Select(Gen.OneOf(Gen.Int[0, 3], Gen.Int[int.MaxValue - 3, int.MaxValue]).Array[1, 5], Gen.UInt, Gen.ULong)
        .Where((weights, _, _) => weights.Sum(w => (long)w) is > 0 and <= uint.MaxValue)
        .Sample((weights, stream, seed) =>
        {
            var constants = Gen.Frequency([.. weights.Select((w, i) => (w, i))]);
            var gens = Gen.Frequency([.. weights.Select((w, i) => (w, Gen.Const(i)))]);
            var pcg = new PCG(stream, seed);
            for (int n = 0; n < 100; n++)
            {
                if (!InRange(constants.Generate(pcg, null, out var size), size.I)) return false;
                if (!InRange(gens.Generate(pcg, null, out size), size.I)) return false;
            }
            return true;

            bool InRange(int i, ulong position)
            {
                var start = weights.Take(i).Sum(w => (long)w);
                return position >= (ulong)start && position < (ulong)(start + weights[i]);
            }
        });
    }

    [Test]
    public void Shuffle()
    {
        Gen.Int.Array.SelectMany(a1 => Gen.Shuffle(a1).Select(a2 => (a1, a2)))
        .Sample((a1, a2) =>
        {
            Array.Sort(a1);
            Array.Sort(a2);
            return Check.Equal(a1, a2);
        });
    }

    [Test]
    public async Task Shuffle_Length_Reaches_Every_Ordered_Selection()
    {
        var source = new[] { 1, 2, 3, 4 };
        var pcg = new PCG(1, 42UL);
        foreach (var (length, expected) in new[] { (1, 4), (2, 12), (3, 24), (4, 24) })
        {
            var gen = Gen.Shuffle(source, length);
            var seen = new HashSet<string>();
            for (int i = 0; i < 4000; i++)
            {
                var a = gen.Generate(pcg, null, out _);
                await Assert.That(a.Length).IsEqualTo(length);
                await Assert.That(a.Distinct().Count()).IsEqualTo(length);
                await Assert.That(a.All(source.Contains)).IsTrue();
                seen.Add(string.Join(",", a));
            }
            await Assert.That(seen.Count).IsEqualTo(expected);
        }
    }

    [Test]
    public void ShuffleSelect_Length_Is_Clamped_To_The_Generators_Available()
    {
        Gen.Int[1, 5].Select(Gen.Int[0, 9])
        .Sample((count, length) =>
        {
            var gens = Enumerable.Range(0, count).Select(i => Gen.Const(i)).ToArray();
            var pcg = new PCG(1, 42UL);
            var expected = Math.Min(length, count);
            var fromArray = gens.ShuffleSelect(length).Generate(pcg, null, out _);
            var fromList = gens.ToList().ShuffleSelect(length).Generate(pcg, null, out _);
            return fromArray.Length == expected && fromList.Count == expected
                && fromArray.Distinct().Count() == expected && fromArray.All(i => i >= 0 && i < count);
        });
    }

    record MyObj(int Id, MyObj[] Children);

    [Test]
    public void RecursiveDepth()
    {
        const int maxDepth = 4;
        Gen.Recursive<MyObj>((i, my) =>
            Gen.Select(Gen.Int, my.Array[0, i < maxDepth ? 6 : 0], (i, a) => new MyObj(i, a))
        )
        .Sample(i =>
        {
            static int Depth(MyObj o) => o.Children.Length == 0 ? 0 : 1 + o.Children.Max(Depth);
            return Depth(i) <= maxDepth;
        });
    }

    [Test]
    public void FastMod()
    {
        Gen.Select(Gen.UInt[0, int.MaxValue], Gen.UInt[1, 2_000_000_000])
        .Sample((value, divisor) =>
        {
            var multiplier = HashHelper.GetFastModMultiplier(divisor);
            var fastMod = HashHelper.FastMod(value, divisor, multiplier);
            return fastMod == value % divisor;
        });
    }
}

[StructLayout(LayoutKind.Explicit)]
internal struct DecimalConverter
{
    public const int ScaleMask = 0x00FF0000;
    public const int SignMask = unchecked((int)0x80000000);
    [FieldOffset(0)] public uint flags;
    [FieldOffset(4)] public uint hi;
    [FieldOffset(8)] public uint mid;
    [FieldOffset(12)] public uint lo;
    [FieldOffset(0)] public decimal D;
}