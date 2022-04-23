// Copyright 2022 Anthony Lloyd
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

using System;
using System.Threading;
using System.Diagnostics;

/// <summary><see href="https://www.pcg-random.org/">PCG</see> is a family of simple fast space-efficient statistically good algorithms for random number generation.</summary>
public class PCG
{
    static int threadCount;
    [ThreadStatic]
    static PCG threadPCG;
    public static PCG ThreadPCG => threadPCG ??= new PCG((uint)Interlocked.Increment(ref threadCount));
    readonly ulong Inc;
    public ulong State;
    public uint Stream => (uint)(Inc >> 1);
    public ulong Seed => State - Inc;
    PCG(ulong inc, ulong state)
    {
        Inc = inc;
        State = state;
    }
    public PCG(uint stream, ulong seed)
    {
        Inc = (stream << 1) | 1UL;
        State = Inc + seed;
    }
    public PCG(uint stream) : this(stream, (ulong)Stopwatch.GetTimestamp()) { }
    public uint Next()
    {
        ulong state = State * 6364136223846793005UL + Inc;
        State = state;
        int rot = (int)(state >> 59);
        uint xorshifted = (uint)((state ^ (state >> 18)) >> 27);
        return (xorshifted >> rot) | (xorshifted << (32 - rot));
    }
    public ulong Next64() => ((ulong)Next() << 32) + Next();
    public uint Next(uint maxExclusive)
    {
        if (maxExclusive == 1U) return 0U;
        var threshold = ((uint)-(int)maxExclusive) % maxExclusive;
        uint n;
        while ((n = Next()) < threshold) ;
        return n % maxExclusive;
    }
    public ulong Next64(ulong maxExclusive)
    {
        if (maxExclusive <= uint.MaxValue) return Next((uint)maxExclusive);
        var threshold = ((ulong)-(long)maxExclusive) % maxExclusive;
        ulong n;
        while ((n = Next64()) < threshold) ;
        return n % maxExclusive;
    }
    public override string ToString() => ToSeedString(State, Stream);
    public string ToString(ulong state) => ToSeedString(state, Stream);
    public static PCG Parse(string seed)
    {
        var (state, stream) = ParseSeedString(seed);
        return new PCG((stream << 1) | 1UL, state);
    }

    static readonly char[] Chars64 = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-".ToCharArray();
    internal static string ToSeedString(ulong i0, uint i1)
    {
        var chars = new char[i1 < 64 ? 12
                           : i1 < 64 * 64 ? 13
                           : i1 < 64 * 64 * 64 ? 14
                           : i1 < 64 * 64 * 64 * 64 ? 15
                           : i1 < 64 * 64 * 64 * 64 * 64 ? 16
                           : 17];
        chars[0] = Chars64[(int)((i0 >> 60) & 63)];
        chars[1] = Chars64[(int)((i0 >> 54) & 63)];
        chars[2] = Chars64[(int)((i0 >> 48) & 63)];
        chars[3] = Chars64[(int)((i0 >> 42) & 63)];
        chars[4] = Chars64[(int)((i0 >> 36) & 63)];
        chars[5] = Chars64[(int)((i0 >> 30) & 63)];
        chars[6] = Chars64[(int)((i0 >> 24) & 63)];
        chars[7] = Chars64[(int)((i0 >> 18) & 63)];
        chars[8] = Chars64[(int)((i0 >> 12) & 63)];
        chars[9] = Chars64[(int)((i0 >> 6) & 63)];
        chars[10] = Chars64[(int)(i0 & 63)];
        chars[11] = Chars64[i1 & 63];
        if (chars.Length > 12)
        {
            chars[12] = Chars64[(i1 >> 6) & 63];
            if (chars.Length > 13)
            {
                chars[13] = Chars64[(i1 >> 12) & 63];
                if (chars.Length > 14)
                {
                    chars[14] = Chars64[(i1 >> 18) & 63];
                    if (chars.Length > 15)
                    {
                        chars[15] = Chars64[(i1 >> 24) & 63];
                        if (chars.Length > 16)
                        {
                            chars[16] = Chars64[(i1 >> 30) & 63];
                        }
                    }
                }
            }
        }
        return new string(chars);
    }
    static int Index(char c)
    {
        int i = Array.IndexOf(Chars64, c);
        if (i == -1) throw new Exception("Invalid seed");
        return i;
    }
    internal static (ulong, uint) ParseSeedString(string seed)
    {
        var i = seed.Length == 12 ? Index(seed[11])
              : seed.Length == 13 ? Index(seed[11]) + (Index(seed[12]) << 6)
              : seed.Length == 14 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12)
              : seed.Length == 15 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18)
              : seed.Length == 16 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18) + (Index(seed[15]) << 24)
              : Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18) + (Index(seed[15]) << 24) + (Index(seed[16]) << 30);
        return ((((((((((((((((((((
              (ulong)Index(seed[0]) << 6)
            + (ulong)Index(seed[1])) << 6)
            + (ulong)Index(seed[2])) << 6)
            + (ulong)Index(seed[3])) << 6)
            + (ulong)Index(seed[4])) << 6)
            + (ulong)Index(seed[5])) << 6)
            + (ulong)Index(seed[6])) << 6)
            + (ulong)Index(seed[7])) << 6)
            + (ulong)Index(seed[8])) << 6)
            + (ulong)Index(seed[9])) << 6)
            + (ulong)Index(seed[10]), (uint)i);
    }
}