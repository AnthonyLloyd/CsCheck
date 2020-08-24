using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;

namespace CsCheck
{
    public static class StreamSerializer
    {
        public static void WriteBool(Stream stream, bool val)
        {
            stream.WriteByte(val ? (byte)1 : (byte)0);
        }
        public static bool ReadBool(Stream stream)
        {
            return stream.ReadByte() == 1;
        }
        public static void WriteSByte(Stream stream, sbyte val)
        {
            stream.WriteByte((byte)val);
        }
        public static sbyte ReadSByte(Stream stream)
        {
            return (sbyte)stream.ReadByte();
        }
        public static void WriteByte(Stream stream, byte val)
        {
            stream.WriteByte(val);
        }
        public static byte ReadByte(Stream stream)
        {
            return (byte)stream.ReadByte();
        }
        public static void WriteShort(Stream stream, short val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
        }
        public static short ReadShort(Stream stream)
        {
            return (short)(stream.ReadByte() + (stream.ReadByte() << 8));
        }
        public static void WriteUShort(Stream stream, ushort val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
        }
        public static ushort ReadUShort(Stream stream)
        {
            return (ushort)(stream.ReadByte() + (stream.ReadByte() << 8));
        }
        public static void WriteInt(Stream stream, int val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
            stream.WriteByte((byte)(val >> 16));
            stream.WriteByte((byte)(val >> 24));
        }
        public static int ReadInt(Stream stream)
        {
            return stream.ReadByte()
                + (stream.ReadByte() << 8)
                + (stream.ReadByte() << 16)
                + (stream.ReadByte() << 24);
        }
        public static void WriteUInt(Stream stream, uint val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
            stream.WriteByte((byte)(val >> 16));
            stream.WriteByte((byte)(val >> 24));
        }
        public static uint ReadUInt(Stream stream)
        {
            return (uint)(stream.ReadByte()
                + (stream.ReadByte() << 8)
                + (stream.ReadByte() << 16)
                + (stream.ReadByte() << 24));
        }
        public static void WriteLong(Stream stream, long val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
            stream.WriteByte((byte)(val >> 16));
            stream.WriteByte((byte)(val >> 24));
            stream.WriteByte((byte)(val >> 32));
            stream.WriteByte((byte)(val >> 40));
            stream.WriteByte((byte)(val >> 48));
            stream.WriteByte((byte)(val >> 56));
        }
        public static long ReadLong(Stream stream)
        {
            return stream.ReadByte()
                + ((long)stream.ReadByte() << 8)
                + ((long)stream.ReadByte() << 16)
                + ((long)stream.ReadByte() << 24)
                + ((long)stream.ReadByte() << 32)
                + ((long)stream.ReadByte() << 40)
                + ((long)stream.ReadByte() << 48)
                + ((long)stream.ReadByte() << 56);
        }
        public static void WriteULong(Stream stream, ulong val)
        {
            stream.WriteByte((byte)val);
            stream.WriteByte((byte)(val >> 8));
            stream.WriteByte((byte)(val >> 16));
            stream.WriteByte((byte)(val >> 24));
            stream.WriteByte((byte)(val >> 32));
            stream.WriteByte((byte)(val >> 40));
            stream.WriteByte((byte)(val >> 48));
            stream.WriteByte((byte)(val >> 56));
        }
        public static ulong ReadULong(Stream stream)
        {
            return (ulong)stream.ReadByte()
                + ((ulong)stream.ReadByte() << 8)
                + ((ulong)stream.ReadByte() << 16)
                + ((ulong)stream.ReadByte() << 24)
                + ((ulong)stream.ReadByte() << 32)
                + ((ulong)stream.ReadByte() << 40)
                + ((ulong)stream.ReadByte() << 48)
                + ((ulong)stream.ReadByte() << 56);
        }
        public static void WriteFloat(Stream stream, float val)
        {
            WriteUInt(stream, new FloatConverter { F = val }.I);
        }
        public static float ReadFloat(Stream stream)
        {
            return new FloatConverter { I = ReadUInt(stream) }.F;
        }
        public static void WriteDouble(Stream stream, double val)
        {
            WriteLong(stream, BitConverter.DoubleToInt64Bits(val));
        }
        public static double ReadDouble(Stream stream)
        {
            return BitConverter.Int64BitsToDouble(ReadLong(stream));
        }
        public static void WriteDecimal(Stream stream, decimal val)
        {
            var c = new DecimalConverter { D = val };
            WriteULong(stream, c.I0);
            WriteULong(stream, c.I1);
        }
        public static decimal ReadDecimal(Stream stream)
        {
            return new DecimalConverter { I0 = ReadULong(stream), I1 = ReadULong(stream) }.D;
        }
        public static void WriteDateTime(Stream stream, DateTime val)
        {
            WriteLong(stream, val.Ticks);
        }
        public static DateTime ReadDateTime(Stream stream)
        {
            return new DateTime(ReadLong(stream));
        }
        public static void WriteTimeSpan(Stream stream, TimeSpan val)
        {
            WriteLong(stream, val.Ticks);
        }
        public static TimeSpan ReadTimeSpan(Stream stream)
        {
            return new TimeSpan(ReadLong(stream));
        }
        public static void WriteDateTimeOffset(Stream stream, DateTimeOffset val)
        {
            WriteDateTime(stream, val.DateTime);
            WriteTimeSpan(stream, val.Offset);
        }
        public static DateTimeOffset ReadDateTimeOffset(Stream stream)
        {
            return new DateTimeOffset(ReadDateTime(stream), ReadTimeSpan(stream));
        }
        public static void WriteGuid(Stream stream, Guid val)
        {
            var c = new GuidConverter { G = val };
            WriteUInt(stream, c.I0);
            WriteUInt(stream, c.I1);
            WriteUInt(stream, c.I2);
            WriteUInt(stream, c.I3);
        }
        public static Guid ReadGuid(Stream stream)
        {
            return new GuidConverter
            {
                I0 = ReadUInt(stream),
                I1 = ReadUInt(stream),
                I2 = ReadUInt(stream),
                I3 = ReadUInt(stream),
            }.G;
        }
        public static void WriteChar(Stream stream, char val)
        {
            var bs = BitConverter.GetBytes(val);
            stream.WriteByte((byte)bs.Length);
            stream.Write(bs, 0, bs.Length);
        }
        public static char ReadChar(Stream stream)
        {
            var l = stream.ReadByte();
            var bs = new byte[l];
            int offset = 0, bytesRead;
            do
            {
                bytesRead = stream.Read(bs, offset, l - offset);
                offset += bytesRead;
            } while (offset != bs.Length && bytesRead != 0);
            return BitConverter.ToChar(bs, 0);
        }
        public static void WriteString(Stream stream, string val)
        {
            var bs = Encoding.Unicode.GetBytes(val);
            WriteInt(stream, bs.Length);
            stream.Write(bs, 0, bs.Length);
        }
        public static string ReadString(Stream stream)
        {
            var l = ReadInt(stream);
            var bs = new byte[l];
            int offset = 0, bytesRead;
            do
            {
                bytesRead = stream.Read(bs, offset, l - offset);
                offset += bytesRead;
            } while (offset != bs.Length && bytesRead != 0);
            return Encoding.Unicode.GetString(bs);
        }
        public static void WriteVarint(Stream stream, uint val)
        {
            if (val < 128u) stream.WriteByte((byte)val);
            else if (val < 0x4000u)
            {
                stream.WriteByte((byte)((val >> 7) | 128u));
                stream.WriteByte((byte)(val & 127u));
            }
            else if (val < 0x200000u)
            {
                stream.WriteByte((byte)((val >> 14) | 128u));
                stream.WriteByte((byte)((val >> 7) | 128u));
                stream.WriteByte((byte)(val & 127u));
            }
            else if (val < 0x10000000u)
            {
                stream.WriteByte((byte)((val >> 21) | 128u));
                stream.WriteByte((byte)((val >> 14) | 128u));
                stream.WriteByte((byte)((val >> 7) | 128u));
                stream.WriteByte((byte)(val & 127u));
            }
            else
            {
                stream.WriteByte((byte)((val >> 28) | 128u));
                stream.WriteByte((byte)((val >> 21) | 128u));
                stream.WriteByte((byte)((val >> 14) | 128u));
                stream.WriteByte((byte)((val >> 7) | 128u));
                stream.WriteByte((byte)(val & 127u));
            }
        }
        public static uint ReadVarint(Stream stream)
        {
            uint i = 0;
            while (true)
            {
                var b = (uint)stream.ReadByte();
                if (b < 128u) return i + b;
                i = (i + (b & 127u)) << 7;
            }
        }
    }

    public class Hash : IDisposable
    {
        static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CsCheck");
        readonly int ExpectedHash;
        readonly Stream stream;
        readonly string filename;
        readonly bool writing;
        bool errorThrown;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Stream<T>(Action<Stream, T> serialize, Func<Stream, T> deserialize, T val)
        {
            if (stream != null)
            {
                if (writing) serialize(stream, val);
                else
                {
                    var val2 = deserialize(stream);
                    if (!val.Equals(val2))
                    {
                        errorThrown = true;
                        throw new CsCheckException($"Actual {val} but Expected {val2}");
                    }
                }
            }
        }
        public static Hash Expected(int? expectedHash,
            [CallerMemberName] string callerMemberName = "", [CallerFilePath] string callerFilePath = "")
        {
            return new Hash(expectedHash, callerMemberName, callerFilePath);
        }
        private Hash(int? expectedHash, string callerMemberName, string callerFilePath)
        {
            if (!expectedHash.HasValue) return;
            filename = callerFilePath.Substring(Path.GetPathRoot(callerFilePath).Length);
            filename = Path.Combine(CacheDir, Path.GetDirectoryName(filename),
                        Path.GetFileNameWithoutExtension(filename) + "__" + callerMemberName + ".chs");
            ExpectedHash = expectedHash.Value;
            if (File.Exists(filename))
            {
                stream = File.Open(filename, FileMode.Open);
                var fileHash = StreamSerializer.ReadInt(stream);
                if (fileHash == ExpectedHash) return;
                stream.Dispose();
            }
            var tempfile = Path.ChangeExtension(filename, "cht");
            if (File.Exists(tempfile)) File.Delete(tempfile);
            Directory.CreateDirectory(Path.GetDirectoryName(tempfile));
            stream = File.Create(tempfile);
            StreamSerializer.WriteInt(stream, ExpectedHash);
            writing = true;
        }
        public void Dispose()
        {
            var actualHash = GetHashCode();
            if (stream != null)
            {
                stream.Dispose();
                if (writing)
                {
                    if (actualHash == ExpectedHash)
                    {
                        if (File.Exists(filename)) File.Delete(filename);
                        File.Move(Path.ChangeExtension(filename, "cht"), filename);
                    }
                    else
                        File.Delete(Path.ChangeExtension(filename, "cht"));
                }
            }
            if (actualHash != ExpectedHash && !errorThrown)
                throw new CsCheckException($"Actual hash {actualHash} but Expected {ExpectedHash}");
        }
        public void Add(bool val)
        {
            Stream(StreamSerializer.WriteBool, StreamSerializer.ReadBool, val);
            AddPrivate(val ? 1 : 0);
        }
        public void Add(sbyte val)
        {
            Stream(StreamSerializer.WriteSByte, StreamSerializer.ReadSByte, val);
            AddPrivate((uint)val);
        }
        public void Add(byte val)
        {
            Stream(StreamSerializer.WriteByte, StreamSerializer.ReadByte, val);
            AddPrivate((uint)val);
        }
        public void Add(short val)
        {
            Stream(StreamSerializer.WriteShort, StreamSerializer.ReadShort, val);
            AddPrivate((uint)val);
        }
        public void Add(ushort val)
        {
            Stream(StreamSerializer.WriteUShort, StreamSerializer.ReadUShort, val);
            AddPrivate((uint)val);
        }
        public void Add(int val)
        {
            Stream((s, i) => StreamSerializer.WriteUInt(s, GenInt.Zigzag(i)),
                   s => GenInt.Unzigzag(StreamSerializer.ReadUInt(s)), val);
            AddPrivate((uint)val);
        }
        public void Add(uint val)
        {
            Stream(StreamSerializer.WriteVarint, StreamSerializer.ReadVarint, val);
            AddPrivate(val);
        }
        public void Add(long val)
        {
            Stream(StreamSerializer.WriteLong, StreamSerializer.ReadLong, val);
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        public void Add(ulong val)
        {
            Stream(StreamSerializer.WriteULong, StreamSerializer.ReadULong, val);
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        public void Add(float val)
        {
            Stream(StreamSerializer.WriteFloat, StreamSerializer.ReadFloat, val);
            AddPrivate(new FloatConverter { F = val }.I);
        }
        public void Add(double val)
        {
            Stream(StreamSerializer.WriteDouble, StreamSerializer.ReadDouble, val);
            AddPrivate(BitConverter.DoubleToInt64Bits(val));
        }
        public void Add(decimal val)
        {
            Stream(StreamSerializer.WriteDecimal, StreamSerializer.ReadDecimal, val);
            var c = new DecimalConverter { D = val };
            AddPrivate(c.I0);
            AddPrivate(c.I1);
        }
        public void Add(DateTime val)
        {
            Stream(StreamSerializer.WriteDateTime, StreamSerializer.ReadDateTime, val);
            AddPrivate(val.Ticks);
        }
        public void Add(TimeSpan val)
        {
            Stream(StreamSerializer.WriteTimeSpan, StreamSerializer.ReadTimeSpan, val);
            AddPrivate(val.Ticks);
        }
        public void Add(DateTimeOffset val)
        {
            Stream(StreamSerializer.WriteDateTimeOffset, StreamSerializer.ReadDateTimeOffset, val);
            AddPrivate(val.DateTime.Ticks);
            AddPrivate(val.Offset.Ticks);
        }
        public void Add(Guid val)
        {
            Stream(StreamSerializer.WriteGuid, StreamSerializer.ReadGuid, val);
            var c = new GuidConverter { G = val };
            AddPrivate(c.I0);
            AddPrivate(c.I1);
            AddPrivate(c.I2);
            AddPrivate(c.I3);
        }
        public void Add(char val)
        {
            Stream(StreamSerializer.WriteChar, StreamSerializer.ReadChar, val);
            AddPrivate((uint)val);
        }
        public void Add(string val)
        {
            Stream(StreamSerializer.WriteString, StreamSerializer.ReadString, val);
            foreach (char c in val) AddPrivate((uint)c);
        }
        public void Add(float val, int decimals)
        {
            Add(Math.Round(val, decimals));
        }
        public void Add(double val, int decimals)
        {
            Add(Math.Round(val, decimals));
        }
        public void Add(decimal val, int decimals)
        {
            Add(Math.Round(val, decimals));
        }
        public void Add(IEnumerable<float> vals, int decimals)
        {
            var array = vals as float[] ?? vals.ToArray();
            Add((uint)array.Length);
            foreach (var val in array)
                Add(val, decimals);
        }
        public void Add(IEnumerable<double> vals, int decimals)
        {
            var array = vals as double[] ?? vals.ToArray();
            Add((uint)array.Length);
            foreach (var val in array)
                Add(val, decimals);
        }
        public void Add(IEnumerable<decimal> vals, int decimals)
        {
            var array = vals as decimal[] ?? vals.ToArray();
            Add((uint)array.Length);
            foreach (var val in array)
                Add(val, decimals);
        }
        #region xxHash implementation from framework HashCode
        const uint Prime1 = 2654435761U;
        const uint Prime2 = 2246822519U;
        const uint Prime3 = 3266489917U;
        const uint Prime4 = 668265263U;
        const uint Prime5 = 374761393U;
        uint _v1 = unchecked(Prime1 + Prime2), _v2 = Prime2, _v3, _v4 = unchecked(0 - Prime1);
        uint _queue1, _queue2, _queue3;
        uint _length;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint RotateLeft(uint value, int count)
        {
            return (value << count) | (value >> (32 - count));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Round(uint hash, uint input)
        {
            return RotateLeft(hash + input * Prime2, 13) * Prime1;
        }
        void AddPrivate(uint val)
        {
            uint previousLength = _length++;
            uint position = previousLength % 4;
            if (position == 0)
                _queue1 = val;
            else if (position == 1)
                _queue2 = val;
            else if (position == 2)
                _queue3 = val;
            else
            {
                _v1 = Round(_v1, _queue1);
                _v2 = Round(_v2, _queue2);
                _v3 = Round(_v3, _queue3);
                _v4 = Round(_v4, val);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddPrivate(int val)
        {
            AddPrivate((uint)val);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddPrivate(long val)
        {
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddPrivate(ulong val)
        {
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint MixEmptyState()
        {
            return Prime5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint MixState(uint v1, uint v2, uint v3, uint v4)
        {
            return RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint QueueRound(uint hash, uint queuedValue)
        {
            return RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;
        }
        public override int GetHashCode()
        {
            uint length = _length;
            uint position = length % 4;
            uint hash = length < 4 ? MixEmptyState() : MixState(_v1, _v2, _v3, _v4);
            hash += length * 4;
            if (position > 0)
            {
                hash = QueueRound(hash, _queue1);
                if (position > 1)
                {
                    hash = QueueRound(hash, _queue2);
                    if (position > 2)
                        hash = QueueRound(hash, _queue3);
                }
            }
            hash = MixFinal(hash);
            return (int)hash;
        }
        #endregion
    }

    public class HashStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        readonly Hash hash = Hash.Expected(null);
        uint bytes;
        int position = 0;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (position == 3)
            {
                if (count == 0) return;
                count--;
                hash.Add(bytes | ((uint)buffer[offset++] << 24));
            }
            else if (position == 2)
            {
                if (count > 1)
                {
                    count -= 2;
                    hash.Add(bytes | ((uint)buffer[offset++] << 16) | ((uint)buffer[offset++] << 24));
                }
                else
                {
                    if (count == 1)
                    {
                        bytes |= (uint)buffer[offset] << 16;
                        position = 3;
                    }
                    return;
                }
            }
            else if (position == 1)
            {
                if (count > 2)
                {
                    count -= 3;
                    hash.Add(bytes | ((uint)buffer[offset++] << 8) | ((uint)buffer[offset++] << 16) | ((uint)buffer[offset++] << 24));
                }
                else
                {
                    if (count == 2)
                    {
                        bytes |= (uint)buffer[offset++] << 8 | (uint)buffer[offset] << 16;
                        position = 3;
                    }
                    else if (count == 1)
                    {
                        bytes |= (uint)buffer[offset] << 8;
                        position = 2;
                    }
                    return;
                }
            }
            int i = offset, lastblock = offset + (count & 0x7ffffffc);
            while (i < lastblock)
                hash.Add(buffer[i++] | ((uint)buffer[i++] << 8) | ((uint)buffer[i++] << 16) | ((uint)buffer[i++] << 24));
            position = offset + count - lastblock;
            if (position > 0)
            {
                bytes = buffer[lastblock];
                if (position > 1)
                {
                    bytes |= (uint)buffer[lastblock + 1] << 8;
                    if (position > 2)
                        bytes |= (uint)buffer[lastblock + 2] << 16;
                }
            }
        }
        public override int GetHashCode()
        {
            if (position > 0) hash.Add(bytes);
            return hash.GetHashCode();
        }
    }
}