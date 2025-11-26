namespace Tests;

using System.Numerics;
using System.Runtime.CompilerServices;
using CsCheck;

public class ArraySerializerTests()
{
    [Test]
    public void Varint()
    {
        Gen.UInt.Sample(i =>
        {
            var bytes = new byte[8];
            int pos = 0;
            ArraySerializer.WriteVarint(bytes, ref pos, i);
            pos = 0;
            return ArraySerializer.ReadVarint(bytes, ref pos) == i;
        });
    }

    [Test]
    public void PrefixVarint()
    {
        Gen.UInt.Sample(i =>
        {
            var bytes = new byte[8];
            int pos = 0;
            ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
            pos = 0;
            return ArraySerializer.ReadPrefixVarint(bytes, ref pos) == i;
        });
    }

    [Test]
    public void Varint_Faster()
    {
        Gen.UInt.Select(Gen.Const(() => new byte[8]))
        .Faster(
            (i, bytes) =>
            {
                int pos = 0;
                ArraySerializer.WriteVarint(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadVarint(bytes, ref pos);
            },
            (i, bytes) =>
            {
                int pos = 0;
                ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadPrefixVarint(bytes, ref pos);
            }, sigma: 10, repeat: 500, raiseexception: false, writeLine: TUnitX.WriteLine);
    }
}

public static class ArraySerializer
{
    public static void WriteVarint(byte[] bytes, ref int pos, uint val)
    {
        if (val < 128u)
        {
            bytes[pos++] = (byte)val;
        }
        else if (val < 0x4000u)
        {
            bytes[pos++] = (byte)((val >> 7) | 128u);
            bytes[pos++] = (byte)(val & 127u);
        }
        else if (val < 0x200000u)
        {
            bytes[pos++] = (byte)((val >> 14) | 128u);
            bytes[pos++] = (byte)((val >> 7) | 128u);
            bytes[pos++] = (byte)(val & 127u);
        }
        else if (val < 0x10000000u)
        {
            bytes[pos++] = (byte)((val >> 21) | 128u);
            bytes[pos++] = (byte)((val >> 14) | 128u);
            bytes[pos++] = (byte)((val >> 7) | 128u);
            bytes[pos++] = (byte)(val & 127u);
        }
        else
        {
            bytes[pos++] = (byte)((val >> 28) | 128u);
            bytes[pos++] = (byte)((val >> 21) | 128u);
            bytes[pos++] = (byte)((val >> 14) | 128u);
            bytes[pos++] = (byte)((val >> 7) | 128u);
            bytes[pos++] = (byte)(val & 127u);
        }
    }

    public static uint ReadVarint(byte[] bytes, ref int pos)
    {
        uint i = 0;
        while (true)
        {
            var b = (uint)bytes[pos++];
            if (b < 128u) return i + b;
            i = (i + (b & 127u)) << 7;
        }
    }

    public static void WritePrefixVarint(byte[] bytes, ref int pos, uint val)
    {
        var noBytes = BitOperations.Log2(val) / 7;
        Unsafe.WriteUnaligned(ref bytes[pos], (((ulong)val << 1) | 1UL) << noBytes);
        pos += noBytes + 1;
    }

    public static uint ReadPrefixVarint(byte[] bytes, ref int pos)
    {
        ulong result = Unsafe.ReadUnaligned<ulong>(ref bytes[pos]);
        var noBytes = BitOperations.TrailingZeroCount(result) + 1;
        pos += noBytes;
        return (uint)(((1UL << (7 * noBytes)) - 1UL) & (result >> noBytes));
    }
}