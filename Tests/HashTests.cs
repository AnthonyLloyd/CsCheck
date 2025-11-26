namespace Tests;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CsCheck;

public class StreamSerializerTests
{
    static void TestRoundtrip<T>(Gen<T> gen, Action<Stream, T> serialize, Func<Stream, T> deserialize)
    {
        gen.Sample(t =>
        {
            using var ms = new MemoryStream();
            serialize(ms, t);
            ms.Position = 0;
            return deserialize(ms)!.Equals(t);
        });
    }
    [Test]
    public void Bool()
    {
        TestRoundtrip(Gen.Bool, Hash.StreamSerializer.WriteBool, Hash.StreamSerializer.ReadBool);
    }
    [Test]
    public void SByte()
    {
        TestRoundtrip(Gen.SByte, Hash.StreamSerializer.WriteSByte, Hash.StreamSerializer.ReadSByte);
    }
    [Test]
    public void Byte()
    {
        TestRoundtrip(Gen.Byte, Hash.StreamSerializer.WriteByte, Hash.StreamSerializer.ReadByte);
    }
    [Test]
    public void Short()
    {
        TestRoundtrip(Gen.Short, Hash.StreamSerializer.WriteShort, Hash.StreamSerializer.ReadShort);
    }
    [Test]
    public void UShort()
    {
        TestRoundtrip(Gen.UShort, Hash.StreamSerializer.WriteUShort, Hash.StreamSerializer.ReadUShort);
    }
    [Test]
    public void Int()
    {
        TestRoundtrip(Gen.Int, Hash.StreamSerializer.WriteInt, Hash.StreamSerializer.ReadInt);
    }
    [Test]
    public void UInt()
    {
        TestRoundtrip(Gen.UInt, Hash.StreamSerializer.WriteUInt, Hash.StreamSerializer.ReadUInt);
    }
    [Test]
    public void Long()
    {
        TestRoundtrip(Gen.Long, Hash.StreamSerializer.WriteLong, Hash.StreamSerializer.ReadLong);
    }
    [Test]
    public void ULong()
    {
        TestRoundtrip(Gen.ULong, Hash.StreamSerializer.WriteULong, Hash.StreamSerializer.ReadULong);
    }
    [Test]
    public void Float()
    {
        TestRoundtrip(Gen.Float, Hash.StreamSerializer.WriteFloat, Hash.StreamSerializer.ReadFloat);
    }
    [Test]
    public void Double()
    {
        TestRoundtrip(Gen.Double, Hash.StreamSerializer.WriteDouble, Hash.StreamSerializer.ReadDouble);
    }
    [Test]
    public void DateTime()
    {
        TestRoundtrip(Gen.DateTime, Hash.StreamSerializer.WriteDateTime, Hash.StreamSerializer.ReadDateTime);
    }
    [Test]
    public void TimeSpan()
    {
        TestRoundtrip(Gen.TimeSpan, Hash.StreamSerializer.WriteTimeSpan, Hash.StreamSerializer.ReadTimeSpan);
    }
    [Test]
    public void DateTimeOffset()
    {
        TestRoundtrip(Gen.DateTimeOffset, Hash.StreamSerializer.WriteDateTimeOffset, Hash.StreamSerializer.ReadDateTimeOffset);
    }
    [Test]
    public void Guid()
    {
        TestRoundtrip(Gen.Guid, Hash.StreamSerializer.WriteGuid, Hash.StreamSerializer.ReadGuid);
    }
    [Test]
    public void Char()
    {
        TestRoundtrip(Gen.Char, Hash.StreamSerializer.WriteChar, Hash.StreamSerializer.ReadChar);
    }
    [Test]
    public void String()
    {
        TestRoundtrip(Gen.String, Hash.StreamSerializer.WriteString, Hash.StreamSerializer.ReadString);
    }
    [Test]
    public void Varint()
    {
        TestRoundtrip(Gen.UInt, Hash.StreamSerializer.WriteVarint, Hash.StreamSerializer.ReadVarint);
    }
}

public class HashTests
{
    [Test]
    public void Hash_Example()
    {
        Check.Hash(hash =>
        {
            var pcg = PCG.Parse("5a7zcxHI4Eg0");
            for (int i = 0; i < 100; i++)
            {
                hash.Add(Gen.Bool.Generate(pcg, null, out _));
                hash.Add(Gen.SByte.Generate(pcg, null, out _));
                hash.Add(Gen.Byte.Generate(pcg, null, out _));
                hash.Add(Gen.Short.Generate(pcg, null, out _));
                hash.Add(Gen.UShort.Generate(pcg, null, out _));
                hash.Add(Gen.Int.Generate(pcg, null, out _));
                hash.Add(Gen.UInt.Generate(pcg, null, out _));
                hash.Add(Gen.Long.Generate(pcg, null, out _));
                hash.Add(Gen.ULong.Generate(pcg, null, out _));
                hash.Add(Gen.Float.Generate(pcg, null, out _));
                hash.Add(Gen.Double.Generate(pcg, null, out _));
                hash.Add(Gen.Decimal.Generate(pcg, null, out _));
                hash.Add(Gen.DateTime.Generate(pcg, null, out _));
                hash.Add(Gen.TimeSpan.Generate(pcg, null, out _));
                hash.Add(Gen.DateTimeOffset.Generate(pcg, null, out _));
                hash.Add(Gen.Guid.Generate(pcg, null, out _));
                hash.Add(Gen.Char.Generate(pcg, null, out _));
                hash.Add(Gen.String.Generate(pcg, null, out _));
            }
        }, 4696400775);
    }

    [Test]
    public void HashStream_Parts()
    {
        Gen.Byte.Array[0, 31].Array[3, 10]
        .Sample(bs =>
        {
            var actual = new HashStream();
            actual.Write(bs[0]);
            var expected = new HashStream();
            expected.Write(bs[0]);
            for (int i = 1; i < bs.Length; i++) actual.Write(bs[i]);
            expected.Write(bs.Skip(1).SelectMany(i => i).ToArray());
            return expected.GetHashCode() == actual.GetHashCode();
        });
    }

    [Test]
    public void Hash_Offset_No_Rounding()
    {
        var h = new Hash(null, -1);
        Assert.Null(h.BestOffset());
    }

    [Test]
    public async Task Hash_Offset_DP_Bottom()
    {
        var h = new Hash(null, -1, decimalPlaces: 1);
        h.Add(1.04);
        h.Add(1.06);
        h.Add(1.09);
        await Assert.That(h.BestOffset()).IsEqualTo(425000000);
    }

    [Test]
    public async Task Hash_Offset_DP_Inner()
    {
        var h = new Hash(null, -1, decimalPlaces: 1);
        h.Add(1.01);
        h.Add(1.03);
        h.Add(1.09);
        await Assert.That(h.BestOffset()).IsEqualTo(200000000);
    }

    [Test]
    public async Task Hash_Offset_DP_Top()
    {
        var h = new Hash(null, -1, decimalPlaces: 1);
        h.Add(1.01);
        h.Add(1.03);
        h.Add(1.05);
        await Assert.That(h.BestOffset()).IsEqualTo(100000001);
    }

    [Test]
    public async Task Hash_Offset_SF_Bottom()
    {
        var h = new Hash(null, -1, significantFigures: 2);
        h.Add(1.04e-7);
        h.Add(1.06e-7);
        h.Add(1.09e-7);
        await Assert.That(h.BestOffset()).IsEqualTo(425000000);
    }

    [Test]
    public async Task Hash_Offset_SF_Inner()
    {
        var h = new Hash(null, -1, significantFigures: 2);
        h.Add(1.01e5);
        h.Add(1.03e3);
        h.Add(1.09e-4);
        await Assert.That(h.BestOffset()).IsEqualTo(200000000);
    }

    [Test]
    public async Task Hash_Offset_SF_Top()
    {
        var h = new Hash(null, -1, significantFigures: 2);
        h.Add(1.01);
        h.Add(1.03);
        h.Add(1.05);
        await Assert.That(h.BestOffset()).IsEqualTo(100000001);
    }

    [Test]
    public async Task Hash_Offset_SF_Zero()
    {
        var h = new Hash(null, -1, significantFigures: 2);
        h.Add(0.0);
        await Assert.That(h.BestOffset()).IsEqualTo(250000000);
    }

    [Test]
    public async Task Hash_No_Offset_Short()
    {
        await Assert.That(Hash.FullHash(null, 0)).IsEqualTo(0x100000000);
    }

    [Test]
    public async Task Hash_Roundtrip_Offset()
    {
        Gen.Int[0, Hash.OFFSET_SIZE - 1].Select(Gen.Int)
        .Sample((offset, hash) =>
        {
            var (offset2, hash2) = Hash.OffsetHash(Hash.FullHash(offset, hash));
            return offset == offset2 && hash == hash2;
        });
    }

    [Test]
    public async Task Hash_Roundtrip_No_Offset()
    {
        Gen.Int
        .Sample(expectedHash =>
        {
            var (offset, hash) = Hash.OffsetHash(Hash.FullHash(null, expectedHash));
            Assert.Null(offset);
            return expectedHash == hash;
        });
    }

    [Test]
    public void Pow10_Double()
    {
        static double Sqr(double x) => x * x;
        static double Pow1(int n) => n switch
        {
            0 => 1.0,
            1 => 10.0,
            2 => 100.0,
            3 => 1000.0,
            4 => 10000.0,
            _ => n % 2 == 0 ? Sqr(Pow1(n / 2)) : Sqr(Pow1(n / 2)) * 10.0,
        };
        static double Pow2(int n)
        {
                double result = 1.0, baseVal = 10.0;
            while (n > 0)
            {
                if ((n & 1) != 0) result *= baseVal;
                n >>= 1;
                baseVal *= baseVal;
            }
            return result;
        }
        double[] powCache = [ 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18,
            1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28, 1e29, 1e30, 1e31 ];
        double Pow3(int n) => powCache[n];
        var genInt = Gen.UInt32.Select(i => (int)i);
        genInt.Faster(Pow1, Pow2, Check.EqualSkip, repeat: 100, writeLine: TUnitX.WriteLine);
        genInt.Faster(Pow3, Pow1, Check.EqualSkip, repeat: 100, writeLine: TUnitX.WriteLine);
        genInt.Faster(Pow3, i => Math.Pow(10, i), Check.EqualSkip, repeat: 100, writeLine: TUnitX.WriteLine);
        genInt.Faster<Pow2Struct, Pow1Struct, int, double>(new(), new(), Check.EqualSkip, repeat: 100, writeLine: TUnitX.WriteLine);
        genInt.Faster<Pow3Struct, Pow2Struct, int, double>(new(), new(), Check.EqualSkip, repeat: 100, writeLine: TUnitX.WriteLine);
    }

    public readonly struct Pow1Struct : IInvoke<int, double>
    {
        static double Sqr(double x) => x * x;
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public double Invoke(int n) => n switch
        {
            0 => 1.0,
            1 => 10.0,
            2 => 100.0,
            3 => 1000.0,
            4 => 10000.0,
            _ => n % 2 == 0 ? Sqr(Invoke(n / 2)) : Sqr(Invoke(n / 2)) * 10.0,
        };
    }

    public readonly struct Pow2Struct : IInvoke<int, double>
    {
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public double Invoke(int n)
        {
            double result = 1.0, baseVal = 10.0;
            while (n > 0)
            {
                if ((n & 1) != 0) result *= baseVal;
                n >>= 1;
                baseVal *= baseVal;
            }
            return result;
        }
    }
    public readonly struct Pow3Struct() : IInvoke<int, double>
    {
        static readonly double[] powCache = [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18,
            1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28, 1e29, 1e30, 1e31];
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public double Invoke(int n) => powCache[n];
    }
}