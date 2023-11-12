namespace Tests;

using System;
using System.Linq;
using System.Runtime.InteropServices;
using CsCheck;
using Xunit;

public class GenTests
{
    static int[] Tally(int n, int[] ia)
    {
        var a = new int[n];
        for (int i = 0; i < ia.Length; i++) a[ia[i]]++;
        return a;
    }

    [Fact]
    public void Bool_Distribution()
    {
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, 2).ToArray();
        Gen.Bool.Select(i => i ? 1 : 0).Array[2 * frequency]
        .Select(sample => Tally(2, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void SByte_Range()
    {
        (from t in Gen.Select(Gen.SByte, Gen.SByte)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.SByte[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void SByte_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.SByte[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Byte_Range()
    {
        (from t in Gen.Byte.Select(Gen.Byte)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Byte[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Byte_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Byte[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Short_Zigzag_Roundtrip()
    {
        Gen.Short.Sample(i => GenShort.Unzigzag(GenShort.Zigzag(i)) == i);
    }

    [Fact]
    public void Short_Range()
    {
        (from t in Gen.Short.Select(Gen.Short)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Short[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Short_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Short[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void UShort_Range()
    {
        (from t in Gen.UShort.Select(Gen.UShort)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.UShort[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void UShort_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.UShort[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Int_Range()
    {
        (from t in Gen.Int.Select(Gen.Int)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Int[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Int_Positive()
    {
        Gen.Int.Positive.Sample(i => i > 0);
    }

    [Fact]
    public void Int_Positive_Gen_Method()
    {
        static (int, ulong) Method(uint s, uint v)
        {
            int i = 1 << (int)s;
            i = (int)v & (i - 1) | i;
            var size = s << 27 | (ulong)i & 0x7FF_FFFFUL;
            return (i, size);
        }
        Assert.Equal((1, 1UL), Method(0U, uint.MaxValue));
        Assert.Equal((1, 1UL), Method(0U, 57686U));
        Assert.Equal((int.MaxValue, 0xF7FF_FFFFUL), Method(30U, uint.MaxValue));
    }

    [Fact]
    public void Short_Gen_Method()
    {
        static (short, ulong) Method(uint s, uint v)
        {
            ushort i = (ushort)(1U << (int)s);
            i = (ushort)((v & (i - 1) | i) - 1);
            var size = s << 11 | i & 0x7FFUL;
            return ((short)-GenShort.Unzigzag(i), size);
        }
        Assert.Equal((0, 0UL), Method(0U, uint.MaxValue));
        Assert.Equal((0, 0UL), Method(0U, 7686U));
        Assert.Equal((1, 0x801UL), Method(1U, uint.MaxValue - 1));
        Assert.Equal((-1, 0x802UL), Method(1U, uint.MaxValue));
        Assert.Equal((short.MaxValue, 0x7FFDUL), Method(15U, uint.MaxValue - 1));
        Assert.Equal((-short.MaxValue, 0x7FFEUL), Method(15U, uint.MaxValue));
    }

    [Fact]
    public void Int_Gen_Method()
    {
        static (int, ulong) Method(uint s, uint v)
        {
            uint i = 1U << (int)s;
            i = (v & (i - 1U) | i) - 1U;
            var size = s << 27 | i & 0x7FF_FFFFUL;
            return (-GenInt.Unzigzag(i), size);
        }
        Assert.Equal((0, 0UL), Method(0U, uint.MaxValue));
        Assert.Equal((0, 0UL), Method(0U, 57686U));
        Assert.Equal((1, 0x800_0001UL), Method(1U, uint.MaxValue-1));
        Assert.Equal((-1, 0x800_0002UL), Method(1U, uint.MaxValue));
        Assert.Equal((int.MaxValue, 0xFFFF_FFFDUL), Method(31U, uint.MaxValue-1));
        Assert.Equal((-int.MaxValue, 0xFFFF_FFFEUL), Method(31U, uint.MaxValue));
    }

    [Fact]
    public void Int_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        int[] expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Int[0, buckets - 1].Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Int_Zigzag_Roundtrip()
    {
        Gen.Int.Sample(i => GenInt.Unzigzag(GenInt.Zigzag(i)) == i);
    }

    [Fact]
    public void Zigzag()
    {
        Assert.Equal(0, GenInt.Unzigzag(0U));
        Assert.Equal(-1, GenInt.Unzigzag(1U));
        Assert.Equal(1, GenInt.Unzigzag(2U));
        Assert.Equal(int.MaxValue, GenInt.Unzigzag(0xFFFFFFFEU));
        Assert.Equal(int.MinValue, GenInt.Unzigzag(0xFFFFFFFFU));
    }

    [Fact]
    public void UInt_Range()
    {
        (from t in Gen.UInt.Select(Gen.UInt)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.UInt[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void UInt_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.UInt[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Long_Zigzag_Roundtrip()
    {
        Gen.Long.Sample(i => GenLong.Unzigzag(GenLong.Zigzag(i)) == i);
    }

    [Fact]
    public void Long_Gen_Method()
    {
        static (long, ulong) Method(uint s, ulong v)
        {
            ulong i = 1UL << (int)s;
            i = (v & (i - 1UL) | i) - 1UL;
            var size = (ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU;
            return (-GenLong.Unzigzag(i), size);
        }
        Assert.Equal((0, 0UL), Method(0, ulong.MaxValue));
        Assert.Equal((0, 0UL), Method(0, 57686));
        Assert.Equal((long.MaxValue, 0xF_FFFF_FFFF_FFFDUL), Method(63U, ulong.MaxValue - 1));
        Assert.Equal((-long.MaxValue, 0xF_FFFF_FFFF_FFFEUL), Method(63U, ulong.MaxValue));
    }

    [Fact]
    public void Long_Range()
    {
        (from t in Gen.Long.Select(Gen.Long)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Long[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Long_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Long[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void ULong_Range()
    {
        (from t in Gen.ULong.Select(Gen.ULong)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.ULong[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void ULong_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.ULong[0, buckets - 1]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Single()
    {
        Gen.Single.Sample(i => i <= float.PositiveInfinity || float.IsNaN(i));
    }

    [Fact]
    public void Single_Unit_Range()
    {
        Gen.Single.Unit.Sample(f => f is >= 0f and <= 0.9999999f);
    }

    [Fact]
    public void Single_Range()
    {
        (from t in Gen.Single.Unit.Select(Gen.Single.Unit)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Single[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Single_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Single.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Double_Unit_Range()
    {
        Gen.Double.Unit.Sample(f => f is >= 0.0 and <= 0.99999999999999978);
    }

    [Fact]
    public void Double_Range()
    {
        (from t in Gen.Double.Unit.Select(Gen.Double.Unit)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Double[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish, seed: "89rtRQWk16go", iter: 1);
    }

    [Fact]
    public void Double_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Double.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Double_Skew()
    {
        (from t in Gen.Double.Unit.Select(Gen.Double.Unit, Gen.Double[-10.0, 10.0])
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Double.Skew[start, finish, t.Item3]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Decimal()
    {
        Gen.Decimal.Sample(i => {
            var c = new DecimalConverter { D = i };
            return (c.flags & ~(DecimalConverter.SignMask | DecimalConverter.ScaleMask)) == 0 && (c.flags & DecimalConverter.ScaleMask) <= (28 << 16);
        });
    }

    [Fact]
    public void Decimal_Unit_Range()
    {
        Gen.Decimal.Unit.Sample(i => i is >= 0.0M and <= 0.99999999999999978M);
    }

    [Fact]
    public void Decimal_Range()
    {
        (from t in Gen.Decimal.Unit.Select(Gen.Decimal.Unit)
         let start = Math.Min(t.Item1, t.Item2)
         let finish = Math.Max(t.Item1, t.Item2)
         from value in Gen.Decimal[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Decimal_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Decimal.Unit
        .Select(i => (int)(i * buckets))
        .Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Date_Range()
    {
        (from t in Gen.Date.Select(Gen.Date)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.Date[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void DateOnly_Range()
    {
        (from t in Gen.DateOnly.Select(Gen.DateOnly)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.DateOnly[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void DateTime_Range()
    {
        (from t in Gen.DateTime.Select(Gen.DateTime)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.DateTime[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void TimeOnly_Range()
    {
        (from t in Gen.TimeOnly.Select(Gen.TimeOnly)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.TimeOnly[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void TimeSpan_Range()
    {
        (from t in Gen.TimeSpan.Select(Gen.TimeSpan)
         let start = t.Item1 < t.Item2 ? t.Item1 : t.Item2
         let finish = t.Item1 < t.Item2 ? t.Item2 : t.Item1
         from value in Gen.TimeSpan[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void DateTimeOffset()
    {
        Gen.DateTimeOffset.Sample(_ => { });
    }

    [Fact]
    public void Guid()
    {
        Gen.Guid.Sample(_ => { });
    }

    [Fact]
    public void Char_Range()
    {
        (from t in Gen.Char.Select(Gen.Char)
         let start = t.Item1 > t.Item2 ? t.Item2 : t.Item1
         let finish = t.Item1 > t.Item2 ? t.Item1 : t.Item2
         from value in Gen.Char[start, finish]
         select (value, start, finish))
        .Sample(i => i.value >= i.start && i.value <= i.finish);
    }

    [Fact]
    public void Char_Distribution()
    {
        const int buckets = 70;
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, buckets).ToArray();
        Gen.Char[(char)0, (char)(buckets - 1)]
        .Select(i => (int)i).Array[frequency * buckets]
        .Select(sample => Tally(buckets, sample))
        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Char_Array()
    {
        const string chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
        Gen.Char[chars].Sample(chars.Contains);
    }

    [Fact]
    public void List()
    {
        Gen.UShort[1, 1000]
        .List[10, 100]
        .Sample(l => l.Count >= 10 && l.Count <= 100
                  && l.All(i => i is >= 1 and <= 1000));
    }

    [Fact]
    public void HashSet()
    {
        Gen.ULong[1, 1000]
        .HashSet[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j is >= 1 and <= 1000));
    }

    [Fact]
    public void Dictionary()
    {
        Gen.Dictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j.Key is >= 1 and <= 1000));
    }

    [Fact]
    public void SortedDictionary()
    {
        Gen.SortedDictionary(Gen.UInt[1, 1000], Gen.Bool)[10, 100]
        .Sample(i => i.Count >= 10 && i.Count <= 100
                  && i.All(j => j.Key is >= 1 and <= 1000));
    }

    [Fact]
    public void OneOfConst()
    {
        Gen.OneOfConst(0, 1, 2).Sample(i => i is >= 0 and <= 2);
    }

    [Fact]
    public void OneOf()
    {
        Gen.OneOf(Gen.Const(0), Gen.Const(1), Gen.Const(2)).Sample(i => i is >= 0 and <= 2);
    }

    [Fact]
    public void Frequency()
    {
        const int frequency = 10;
        (from f in Gen.Select(Gen.Int[1, 5], Gen.Int[1, 5], Gen.Int[1, 5])
         let expected = new[] { f.Item1 * frequency, f.Item2 * frequency, f.Item3 * frequency }
         from actual in Gen.FrequencyConst((f.Item1, 0), (f.Item2, 1), (f.Item3, 2))
                        .Array[frequency * (f.Item1 + f.Item2 + f.Item3)]
                        .Select(sample => Tally(3, sample))
         select (expected, actual))
        .Sample(t => Check.ChiSquared(t.expected, t.actual, 10), iter: 1, time: -2);
    }

    [Fact]
    public void Shuffle()
    {
        Gen.Int.Array.SelectMany(a1 => Gen.Shuffle(a1).Select(a2 => (a1, a2)))
        .Sample((a1, a2) =>
        {
            Array.Sort(a1);
            Array.Sort(a2);
            Assert.Equal(a1, a2);
        });
    }

    record MyObj(int Id, MyObj[] Children);

    [Fact]
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

    [Fact]
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