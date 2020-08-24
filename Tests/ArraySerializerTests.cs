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

        [Fact]
        public void SQLite()
        {
            Gen.UInt.Sample(i =>
            {
                var bytes = new byte[8];
                int pos = 0;
                ArraySerializer.WriteSQLite(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadSQLite(bytes, ref pos) == i;
            });
        }

        [Fact]
        public void PrefixVarint_Perf()
        {
            var bytes = new byte[9];
            Gen.UInt.Faster(i =>
            {
                int pos = 1;
                ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
                pos = 1;
                return ArraySerializer.ReadPrefixVarint(bytes, ref pos) == i;
            }
            , i =>
            {
                int pos = 1;
                ArraySerializer.WriteVarint(bytes, ref pos, i);
                pos = 1;
                return ArraySerializer.ReadVarint(bytes, ref pos) == i;
            }, threads: 1, sigma: 50, repeat: 10_000)
            .Output(writeLine);
        }

        [Fact]
        public void SQLite_Perf()
        {
            var bytes = new byte[8];
            Gen.UInt.Faster(i =>
            {
                int pos = 0;
                ArraySerializer.WriteSQLite(bytes, ref pos, i);
                pos = 0;
                return i == ArraySerializer.ReadSQLite(bytes, ref pos);
            }
            , i =>
            {
                int pos = 0;
                ArraySerializer.WriteVarint(bytes, ref pos, i);
                pos = 0;
                return ArraySerializer.ReadVarint(bytes, ref pos) == i;
            }
            , threads: 1, sigma: 50, repeat: 10_000)
            .Output(writeLine);
        }
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

        public static void WriteSQLite(byte[] bytes, ref int pos, uint val)
        {
            //const uint X = 128u;
            //const uint Y= ((252u - X) << 8) + 256u + X;
            if (val < 128u) bytes[pos++] = (byte)val;
            else if (val < 32128u)
            {
                bytes[pos++] = (byte)(((val - 128u) >> 8) + 128u);
                bytes[pos++] = (byte)(val - 128u);
            }
            else if (val < 0x10000u)
            {
                bytes[pos++] = 253;
                bytes[pos++] = (byte)(val >> 8);
                bytes[pos++] = (byte)val;
            }
            else if (val < 0x1000000u)
            {
                bytes[pos++] = 254;
                bytes[pos++] = (byte)(val >> 16);
                bytes[pos++] = (byte)(val >> 8);
                bytes[pos++] = (byte)val;
            }
            else
            {
                bytes[pos++] = 255;
                bytes[pos++] = (byte)(val >> 24);
                bytes[pos++] = (byte)(val >> 16);
                bytes[pos++] = (byte)(val >> 8);
                bytes[pos++] = (byte)val;
            }
        }

        public static uint ReadSQLite(byte[] bytes, ref int pos)
        {
            //const uint X = 128u;
            uint A0 = bytes[pos++];
            return A0 < 128u ? A0
                 : A0 < 253u ? ((A0 - 128u) << 8) + 128u + bytes[pos++]
                 : A0 == 253u ? ((uint)bytes[pos++] << 8) + bytes[pos++]
                 : A0 == 254u ? ((uint)bytes[pos++] << 16) + ((uint)bytes[pos++] << 8) + bytes[pos++]
                 : ((uint)bytes[pos++] << 24) + ((uint)bytes[pos++] << 16) + ((uint)bytes[pos++] << 8) + bytes[pos++];
        }

        public static void WritePrefixVarint(byte[] bytes, ref int pos, uint val)
        {
            var neededBytes = BitOperations.Log2(val) / 7;
            Unsafe.WriteUnaligned(ref bytes[pos], (((ulong)val << 1) | 1UL) << neededBytes);
            pos += neededBytes + 1;
        }

        public static uint ReadPrefixVarint(byte[] bytes, ref int pos)
        {
            ulong result = Unsafe.ReadUnaligned<ulong>(ref bytes[pos]);
            var bytesNeeded = BitOperations.TrailingZeroCount(result) + 1;
            pos += bytesNeeded;
            return (uint)(((1UL << (bytesNeeded * 7)) - 1UL) & (result >> bytesNeeded));
        }
    }
}
