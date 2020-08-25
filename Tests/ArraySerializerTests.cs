using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Xunit;
using CsCheck;

namespace Tests
{
    public class ArraySerializerTests
    {
        readonly Action<string> writeLine;
        public ArraySerializerTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        [Fact]
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

        [Fact]
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

        void PrefixVarint_Faster(double skew)
        {
            var bytes = new byte[8];
            Gen.UInt.Skew[skew].Faster(i =>
            {
                int pos = 0;
                ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadPrefixVarint(bytes, ref pos);
            }
            , i =>
            {
                int pos = 0;
                ArraySerializer.WriteVarint(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadVarint(bytes, ref pos);
            }, threads: 1, sigma: 50, repeat: 10_000)
            .Output(writeLine);
        }
        [Fact]
        public void PrefixVarint_Faster_NoSkew() => PrefixVarint_Faster(0);
        [Fact]
        public void PrefixVarint_Faster_Skew10() => PrefixVarint_Faster(5);
    }

    public static class ArraySerializer
    {
        public static void WriteVarint(byte[] bytes, ref int pos, uint val)
        {
            if (val < 128u) bytes[pos++] = (byte)val;
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
            return (uint)(((1UL << (noBytes * 7)) - 1UL) & (result >> noBytes));
        }
    }
}
