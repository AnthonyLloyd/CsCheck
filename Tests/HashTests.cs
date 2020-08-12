using System;
using System.IO;
using Xunit;
using CsCheck;
using System.Linq;
using System.Threading;

namespace Tests
{
    public class StreamSerializerTests
    {
        static void TestRoundtrip<T>(Gen<T> gen, Action<Stream, T> serialize, Func<Stream, T> deserialize)
        {
            gen.Sample(t =>
            {
                using var ms = new MemoryStream();
                serialize(ms, t);
                ms.Position = 0;
                return deserialize(ms).Equals(t);
            });
        }
        [Fact]
        public void Bool()
        {
            TestRoundtrip(Gen.Bool, StreamSerializer.WriteBool, StreamSerializer.ReadBool);
        }
        [Fact]
        public void SByte()
        {
            TestRoundtrip(Gen.SByte, StreamSerializer.WriteSByte, StreamSerializer.ReadSByte);
        }
        [Fact]
        public void Byte()
        {
            TestRoundtrip(Gen.Byte, StreamSerializer.WriteByte, StreamSerializer.ReadByte);
        }
        [Fact]
        public void Short()
        {
            TestRoundtrip(Gen.Short, StreamSerializer.WriteShort, StreamSerializer.ReadShort);
        }
        [Fact]
        public void UShort()
        {
            TestRoundtrip(Gen.UShort, StreamSerializer.WriteUShort, StreamSerializer.ReadUShort);
        }
        [Fact]
        public void Int()
        {
            TestRoundtrip(Gen.Int, StreamSerializer.WriteInt, StreamSerializer.ReadInt);
        }
        [Fact]
        public void UInt()
        {
            TestRoundtrip(Gen.UInt, StreamSerializer.WriteUInt, StreamSerializer.ReadUInt);
        }
        [Fact]
        public void Long()
        {
            TestRoundtrip(Gen.Long, StreamSerializer.WriteLong, StreamSerializer.ReadLong);
        }
        [Fact]
        public void ULong()
        {
            TestRoundtrip(Gen.ULong, StreamSerializer.WriteULong, StreamSerializer.ReadULong);
        }
        [Fact]
        public void Float()
        {
            TestRoundtrip(Gen.Float, StreamSerializer.WriteFloat, StreamSerializer.ReadFloat);
        }
        [Fact]
        public void Double()
        {
            TestRoundtrip(Gen.Double, StreamSerializer.WriteDouble, StreamSerializer.ReadDouble);
        }
        [Fact]
        public void DateTime()
        {
            TestRoundtrip(Gen.DateTime, StreamSerializer.WriteDateTime, StreamSerializer.ReadDateTime);
        }
        [Fact]
        public void TimeSpan()
        {
            TestRoundtrip(Gen.TimeSpan, StreamSerializer.WriteTimeSpan, StreamSerializer.ReadTimeSpan);
        }
        [Fact]
        public void DateTimeOffset()
        {
            TestRoundtrip(Gen.DateTimeOffset, StreamSerializer.WriteDateTimeOffset, StreamSerializer.ReadDateTimeOffset);
        }
        [Fact]
        public void Guid()
        {
            TestRoundtrip(Gen.Guid, StreamSerializer.WriteGuid, StreamSerializer.ReadGuid);
        }
        [Fact]
        public void Char()
        {
            TestRoundtrip(Gen.Char, StreamSerializer.WriteChar, StreamSerializer.ReadChar);
        }
        [Fact]
        public void String()
        {
            TestRoundtrip(Gen.String, StreamSerializer.WriteString, StreamSerializer.ReadString);
        }
        [Fact]
        public void VarInt()
        {
            TestRoundtrip(Gen.UInt, StreamSerializer.WriteVarInt, StreamSerializer.ReadVarInt);
        }
        [Fact(Skip = "WIP")]
        public void VarInt_Perf()
        {
            var ms = new MemoryStream(10);
            Gen.UInt.Faster(
                i =>
                {
                    ms.Position = 0;
                    StreamSerializer.WriteVarInt2(ms, i);
                    ms.Position = 0;
                    return StreamSerializer.ReadVarInt2(ms);
                },
                i =>
                {
                    ms.Position = 0;
                    StreamSerializer.WriteVarInt(ms, i);
                    ms.Position = 0;
                    return StreamSerializer.ReadVarInt(ms);
                }
            , sigma: 20, threads: 1);
        }
    }

    public class HashTests
    {
        [Fact]
        public void Hash_Example()
        {
            var pcg = PCG.Parse("5a7zcxHI4Eg0");
            using var hash = Hash.Expected(35072759);
            for (int i = 0; i < 100; i++)
            {
                hash.Add(Gen.Bool.Generate(pcg).Item1);
                hash.Add(Gen.SByte.Generate(pcg).Item1);
                hash.Add(Gen.Byte.Generate(pcg).Item1);
                hash.Add(Gen.Short.Generate(pcg).Item1);
                hash.Add(Gen.UShort.Generate(pcg).Item1);
                hash.Add(Gen.Int.Generate(pcg).Item1);
                hash.Add(Gen.UInt.Generate(pcg).Item1);
                hash.Add(Gen.Long.Generate(pcg).Item1);
                hash.Add(Gen.ULong.Generate(pcg).Item1);
                hash.Add(Gen.Float.Generate(pcg).Item1);
                hash.Add(Gen.Double.Generate(pcg).Item1);
                hash.Add(Gen.Decimal.Generate(pcg).Item1);
                hash.Add(Gen.DateTime.Generate(pcg).Item1);
                hash.Add(Gen.TimeSpan.Generate(pcg).Item1);
                hash.Add(Gen.DateTimeOffset.Generate(pcg).Item1);
                hash.Add(Gen.Guid.Generate(pcg).Item1);
                hash.Add(Gen.Char.Generate(pcg).Item1);
                hash.Add(Gen.String.Generate(pcg).Item1);
            }
        }

        [Fact]
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
                Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
            });
        }
    }
}