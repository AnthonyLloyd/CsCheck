using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace CsCheck
{
    public class Hash
    {
        const int Reading = 1, Writing = 2;
        int status;
        void Check<T>(T val)
        {
            if (status == Writing)
            {

            }
            else if (status == Reading)
            {

            }
        }
        public Hash([CallerMemberName] string callerMemberName = "", [CallerFilePath] string callerFilePath = "")
        {
            //var store = System.IO.IsolatedStorage.IsolatedStorageFile.GetUserStoreForAssembly();
            //var x = new StreamWriter(store.CreateFile(""));
            //x.WriteLine();
            status = 1;
        }
        public void Add(bool val)
        {
            Check(val);
            AddPrivate(val ? 1 : 0);
        }
        public void Add(sbyte val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(byte val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(short val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(ushort val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(int val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(uint val)
        {
            Check(val);
            AddPrivate(val);
        }
        public void Add(long val)
        {
            Check(val);
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        public void Add(ulong val)
        {
            Check(val);
            AddPrivate((uint)val);
            AddPrivate((uint)(val >> 32));
        }
        public void Add(float val)
        {
            Check(val);
            AddPrivate(new FloatConverter { F = val }.I);
        }
        public void Add(double val)
        {
            Check(val);
            AddPrivate(BitConverter.DoubleToInt64Bits(val));
        }
        public void Add(decimal val)
        {
            Check(val);
            var c = new DecimalConverter { D = val };
            AddPrivate(c.I0);
            AddPrivate(c.I1);
        }
        public void Add(DateTime val)
        {
            Check(val);
            AddPrivate(val.Ticks);
        }
        public void Add(TimeSpan val)
        {
            Check(val);
            AddPrivate(val.Ticks);
        }
        public void Add(DateTimeOffset val)
        {
            Check(val);
            AddPrivate(val.DateTime.Ticks);
            AddPrivate(val.Offset.Ticks);
        }
        public void Add(Guid val)
        {
            Check(val);
            var c = new GuidConverter { G = val };
            AddPrivate(c.I0);
            AddPrivate(c.I1);
            AddPrivate(c.I2);
            AddPrivate(c.I3);
        }
        public void Add(char val)
        {
            Check(val);
            AddPrivate((uint)val);
        }
        public void Add(string val)
        {
            Check(val);
            foreach (char c in val) AddPrivate((uint)c);
        }
        public void Add(double val, int decimals)
        {
            Add(Math.Round(val, decimals));
        }
        public void Add(IEnumerable<double> vals, int decimals)
        {
            foreach (var val in vals)
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
        public int ToHashCode()
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

#pragma warning disable 0809
        [Obsolete("Use ToHashCode", error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode() => throw new NotSupportedException();
        [Obsolete("Not supported", error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object o) => throw new NotSupportedException();
#pragma warning restore 0809
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
        Hash hash = new Hash();
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
        public int ToHashCode()
        {
            if (position > 0) hash.Add(bytes);
            return hash.ToHashCode();
        }
#pragma warning disable 0809
        [Obsolete("Use ToHashCode", error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode() => throw new NotSupportedException();
        [Obsolete("Not supported", error: true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object o) => throw new NotSupportedException();
#pragma warning restore 0809
    }
}