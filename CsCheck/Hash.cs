// Copyright 2026 Anthony Lloyd
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace CsCheck;

using System.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public interface IRegression
{
    void Add(bool val);
    void Add(byte val);
    void Add(char val);
    void Add(DateTime val);
    void Add(DateTimeOffset val);
    void Add(decimal val);
    void Add(double val);
    void Add(float val);
    void Add(Guid val);
    void Add(int val);
    void Add(long val);
    void Add(sbyte val);
    void Add(short val);
    void Add(string val);
    void Add(TimeSpan val);
    void Add(uint val);
    void Add(ulong val);
    void Add(ushort val);
}

/// <summary>Functionality for hash testing data with detailed information of any changes.</summary>
public sealed class Hash : IRegression
{
    static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> replaceLock = new(StringComparer.Ordinal);
    internal static readonly string CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CsCheck");
    public const int OFFSET_SIZE = 500_000_000;
    readonly int Offset;
    readonly int? DecimalPlaces, SignificantFigures;
    readonly int ExpectedHash;
    readonly Stream? stream;
    readonly string? filename;
    readonly string? threadId;
    readonly bool writing;
    readonly List<int>? roundingFractions;
    string lastString = "null";
    string LastString => string.Equals(lastString, "null") ? "null" : $"'{lastString}'";
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Stream<T>(Action<Stream, T> serialize, Func<Stream, T> deserialize, T val) where T : notnull
    {
        if (stream is not null)
        {
            if (writing)
            {
                serialize(stream, val);
            }
            else
            {
                var val2 = deserialize(stream);
                if (!val.Equals(val2))
                    throw new CsCheckException($"Actual '{val}' but Expected '{val2}'. (last string was {LastString})");
            }
        }
    }

    public Hash(int? expectedHash, int? offset = null, int? decimalPlaces = null, int? significantFigures = null, string memberName = "", string filePath = "")
    {
        Offset = offset ?? 0;
        DecimalPlaces = decimalPlaces;
        SignificantFigures = significantFigures;
        if (offset == -1)
        {
            roundingFractions = [];
            return;
        }
        if (!expectedHash.HasValue) return;
        ExpectedHash = expectedHash.Value;
        filename = Filename(FullHash(offset, ExpectedHash), memberName, filePath);
        var rwLock = replaceLock.GetOrAdd(filename, _ => new ReaderWriterLockSlim());
        rwLock.EnterUpgradeableReadLock();
        if (File.Exists(filename))
        {
            stream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read);
            return;
        }
        rwLock.EnterWriteLock();
        threadId = Environment.CurrentManagedThreadId.ToString();
        var tempfile = filename + threadId;
        if (File.Exists(tempfile)) File.Delete(tempfile);
        Directory.CreateDirectory(Path.GetDirectoryName(tempfile)!);
        stream = File.Create(tempfile);
        writing = true;
    }

    internal static string Filename(long expectedHashCode, string memberName, string filePath)
    {
        filePath = filePath[Path.GetPathRoot(filePath)!.Length..];
        return Path.Combine(CacheDir, Path.GetDirectoryName(filePath)!,
            Path.GetFileNameWithoutExtension(filePath) + "." + memberName + "=" + expectedHashCode + ".has");
    }

    // The hash code is stored in the lower 32 bits for both with and without offset.
    // The 33rd bit is a flag for no offset. Meaning a range of values for no offset of (0x100000000,0x1FFFFFFFF) = (4_294_967_296,8_589_934_591) ie 10 digits always.
    // The offset is shifted 33 bits and the bit above this set giving a range of (0x4000000000000000,(500_000_000 < 33) | 0x4000000000000000) | 0xFFFFFFFF)
    // = (4_611_686_018_427_387_904,8_906_653_318_722_355_199)
    public static long FullHash(int? offset, int hash)
    {
        return offset.HasValue ? ((((long)offset) << 33) | 0x4000000000000000) + (uint)hash : 0x100000000 | (uint)hash;
    }

    public static (int?, int) OffsetHash(long fullHash)
    {
        return ((fullHash & 0x100000000) == 0 ? (int?)((fullHash & 0x3FFFFFFE00000000) >> 33) : null, (int)fullHash);
    }

    public int? BestOffset()
    {
        if (roundingFractions is null || roundingFractions.Count == 0) return null;
        roundingFractions.Sort();
        var maxDiff = OFFSET_SIZE - roundingFractions[^1] + roundingFractions[0];
        var maxMid = roundingFractions[^1] + maxDiff / 2;
        if (maxMid >= OFFSET_SIZE) maxMid -= OFFSET_SIZE;
        for (int i = 1; i < roundingFractions.Count; i++)
        {
            var diff = roundingFractions[i] - roundingFractions[i - 1];
            if (diff > maxDiff)
            {
                maxDiff = diff;
                maxMid = roundingFractions[i - 1] + maxDiff / 2;
            }
        }
        return OFFSET_SIZE - maxMid;
    }

    public void Close()
    {
        var actualHash = GetHashCode();
        if (stream is not null)
        {
            stream.Dispose();

            if (writing)
            {
                if (actualHash == ExpectedHash)
                {
                    if (File.Exists(filename)) File.Delete(filename);
                    File.Move(filename + threadId, filename!);
                }
                else
                {
                    File.Delete(filename + threadId);
                }

                replaceLock[filename!].ExitWriteLock();
            }
            replaceLock[filename!].ExitUpgradeableReadLock();
        }
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
    [StructLayout(LayoutKind.Explicit)]
    struct GuidConverter
    {
        [FieldOffset(0)] public Guid G;
        [FieldOffset(0)] public uint I0;
        [FieldOffset(4)] public uint I1;
        [FieldOffset(8)] public uint I2;
        [FieldOffset(12)] public uint I3;
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
        lastString = val;
    }

    public void Add(double val)
    {
        if (Offset == -1)
        {
            if (DecimalPlaces.HasValue)
            {
                val *= Pow10Double(DecimalPlaces.Value);
                roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
            }
            else if (SignificantFigures.HasValue)
            {
                if (val == 0.0)
                {
                    roundingFractions!.Add(0);
                }
                else
                {
                    val *= Pow10Double(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10(Math.Abs(val))));
                    roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
                }
            }
        }
        else
        {
            if (DecimalPlaces.HasValue)
            {
                var scale = Pow10Double(DecimalPlaces.Value);
                val = Math.Floor(val * scale + ((double)Offset / OFFSET_SIZE)) / scale;
            }
            else if (SignificantFigures.HasValue)
            {
                if (val != 0.0)
                {
                    var scale = Pow10Double(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10(Math.Abs(val))));
                    val = Math.Floor(val * scale + ((double)Offset / OFFSET_SIZE)) / scale;
                }
            }
            Stream(StreamSerializer.WriteDouble, StreamSerializer.ReadDouble, val);
            AddPrivate(BitConverter.DoubleToInt64Bits(val));
        }
    }
    [StructLayout(LayoutKind.Explicit)]
    struct FloatConverter
    {
        [FieldOffset(0)] public uint I;
        [FieldOffset(0)] public float F;
    }
    public void Add(float val)
    {
        if (Offset == -1)
        {
            if (DecimalPlaces.HasValue)
            {
                val *= Pow10Float(DecimalPlaces.Value);
                roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
            }
            else if (SignificantFigures.HasValue)
            {
                if (val == 0.0f)
                {
                    roundingFractions!.Add(0);
                }
                else
                {
                    val *= Pow10Float(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10(Math.Abs(val))));
                    roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
                }
            }
        }
        else
        {
            if (DecimalPlaces.HasValue)
            {
                var scale = Pow10Float(DecimalPlaces.Value);
                val = (float)Math.Floor(val * scale + ((float)Offset / OFFSET_SIZE)) / scale;
            }
            else if (SignificantFigures.HasValue)
            {
                if (val != 0.0f)
                {
                    var scale = Pow10Float(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10(Math.Abs(val))));
                    val = (float)Math.Floor(val * scale + ((float)Offset / OFFSET_SIZE)) / scale;
                }
            }
            Stream(StreamSerializer.WriteFloat, StreamSerializer.ReadFloat, val);
            AddPrivate(new FloatConverter { F = val }.I);
        }
    }
    [StructLayout(LayoutKind.Explicit)]
    struct DecimalConverter
    {
        [FieldOffset(0)] public decimal D;
        [FieldOffset(0)] public uint flags;
        [FieldOffset(4)] public uint hi;
        [FieldOffset(8)] public uint mid;
        [FieldOffset(12)] public uint lo;
    }
    public void Add(decimal val)
    {
        if (Offset == -1)
        {
            if (DecimalPlaces.HasValue)
            {
                val *= Pow10Decimal(DecimalPlaces.Value);
                roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
            }
            else if (SignificantFigures.HasValue)
            {
                if (val == 0.0M)
                {
                    roundingFractions!.Add(0);
                }
                else
                {
                    val *= Pow10Decimal(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10((double)Math.Abs(val))));
                    roundingFractions!.Add((int)((val - Math.Floor(val)) * OFFSET_SIZE));
                }
            }
        }
        else
        {
            if (DecimalPlaces.HasValue)
            {
                var scale = Pow10Decimal(DecimalPlaces.Value);
                val = Math.Floor(val * scale + ((decimal)Offset / OFFSET_SIZE)) / scale;
            }
            else if (SignificantFigures.HasValue)
            {
                if (val != 0.0M)
                {
                    var scale = Pow10Decimal(SignificantFigures.Value - 1 - (int)Math.Floor(Math.Log10((double)Math.Abs(val))));
                    val = Math.Floor(val * scale + ((decimal)Offset / OFFSET_SIZE)) / scale;
                }
            }
            Stream(StreamSerializer.WriteDecimal, StreamSerializer.ReadDecimal, val);
            var c = new DecimalConverter { D = val };
            AddPrivate(c.flags);
            AddPrivate(c.hi);
            AddPrivate(c.mid);
            AddPrivate(c.lo);
        }
    }

    static readonly double[] pow10Double = [ 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14,
        1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28, 1e29, 1e30, 1e31 ];
    static double Pow10Double(int i) => i >= 0 ? pow10Double[i] : 1.0 / pow10Double[-i];
    static readonly float[] pow10Float = [ 1e0f, 1e1f, 1e2f, 1e3f, 1e4f, 1e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e10f, 1e11f, 1e12f, 1e13f, 1e14f,
        1e15f, 1e16f, 1e17f, 1e18f, 1e19f, 1e20f, 1e21f, 1e22f, 1e23f, 1e24f, 1e25f, 1e26f, 1e27f, 1e28f, 1e29f, 1e30f, 1e31f ];
    static float Pow10Float(int i) => i >= 0 ? pow10Float[i] : 1.0f / pow10Float[-i];
    static readonly decimal[] pow10Decimal = [ 1e0M, 1e1M, 1e2M, 1e3M, 1e4M, 1e5M, 1e6M, 1e7M, 1e8M, 1e9M, 1e10M, 1e11M, 1e12M, 1e13M, 1e14M,
        1e15M, 1e16M, 1e17M, 1e18M, 1e19M, 1e20M, 1e21M, 1e22M, 1e23M, 1e24M, 1e25M, 1e26M, 1e27M, 1e28M ];
    static decimal Pow10Decimal(int i) => i >= 0 ? pow10Decimal[i] : 1.0M / pow10Decimal[-i];

    internal static class StreamSerializer
    {
        public static void WriteBool(Stream stream, bool val)
        {
            stream.WriteByte((byte)(val ? 1 : 0));
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
            WriteUShort(stream, (ushort)(c.flags >> 16));
            WriteUInt(stream, c.hi);
            WriteUInt(stream, c.mid);
            WriteUInt(stream, c.lo);
        }
        public static decimal ReadDecimal(Stream stream)
        {
            return new DecimalConverter
            {
                flags = (uint)ReadUShort(stream) << 16,
                hi = ReadUInt(stream),
                mid = ReadUInt(stream),
                lo = ReadUInt(stream),
            }.D;
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
            var bs = Encoding.UTF8.GetBytes(val);
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
            return Encoding.UTF8.GetString(bs);
        }
        public static void WriteVarint(Stream stream, uint val)
        {
            if (val < 128u)
            {
                stream.WriteByte((byte)val);
            }
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
        {
            _queue1 = val;
        }
        else if (position == 1)
        {
            _queue2 = val;
        }
        else if (position == 2)
        {
            _queue3 = val;
        }
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

/// <summary>A stream for hash testing.</summary>
public sealed class HashStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
    readonly Hash hash = new(null);
    uint bytes;
    int position;
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