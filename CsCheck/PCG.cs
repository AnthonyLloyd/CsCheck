// Copyright 2023 Anthony Lloyd
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
using System.Numerics;

/// <summary><see href="https://www.pcg-random.org/">PCG</see> is a family of simple fast space-efficient statistically good algorithms for random number generation.</summary>
public sealed class PCG
{
    static int threadCount;
    [ThreadStatic] static PCG? threadPCG;
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
        return BitOperations.RotateRight(
            (uint)((state ^ (state >> 18)) >> 27),
            (int)(state >> 59));
    }
    public ulong Next64() => ((ulong)Next() << 32) + Next();
    public uint Next(uint maxExclusive)
    {
        if (maxExclusive == 1U) return 0U;
        var threshold = ((uint)-(int)maxExclusive) % maxExclusive;
        var n = Next();
        while (n < threshold) n = Next();
        return n % maxExclusive;
    }
    public ulong Next64(ulong maxExclusive)
    {
        if (maxExclusive <= uint.MaxValue) return Next((uint)maxExclusive);
        var threshold = ((ulong)-(long)maxExclusive) % maxExclusive;
        var n = Next64();
        while (n < threshold) n = Next64();
        return n % maxExclusive;
    }
    public uint Next(uint maxExclusive, ulong multiplier)
    {
        if (maxExclusive == 1U) return 0U;
        var threshold = HashHelper.FastMod((uint)-(int)maxExclusive, maxExclusive, multiplier);
        var n = Next();
        while (n < threshold) n = Next();
        return HashHelper.FastMod(n, maxExclusive, multiplier);
    }
    public override string ToString() => SeedString.ToString(State, Stream);
    public string ToString(ulong state) => SeedString.ToString(state, Stream);
    public static PCG Parse(string seed)
    {
        var state = SeedString.Parse(seed, out var stream);
        return new PCG((stream << 1) | 1UL, state);
    }
}

internal static class SeedString
{
    static readonly char[] Chars64 = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-".ToCharArray();
    internal static string ToString(ulong state, uint stream)
    {
        return string.Create(
              stream < 64 ? 12
            : stream < 64 * 64 ? 13
            : stream < 64 * 64 * 64 ? 14
            : stream < 64 * 64 * 64 * 64 ? 15
            : stream < 64 * 64 * 64 * 64 * 64 ? 16
            : 17,
            (state, stream),
            (chars, value) =>
            {
                var (state, stream) = value;
                chars[0] = Chars64[(int)((state >> 60) & 63)];
                chars[1] = Chars64[(int)((state >> 54) & 63)];
                chars[2] = Chars64[(int)((state >> 48) & 63)];
                chars[3] = Chars64[(int)((state >> 42) & 63)];
                chars[4] = Chars64[(int)((state >> 36) & 63)];
                chars[5] = Chars64[(int)((state >> 30) & 63)];
                chars[6] = Chars64[(int)((state >> 24) & 63)];
                chars[7] = Chars64[(int)((state >> 18) & 63)];
                chars[8] = Chars64[(int)((state >> 12) & 63)];
                chars[9] = Chars64[(int)((state >> 6) & 63)];
                chars[10] = Chars64[(int)(state & 63)];
                chars[11] = Chars64[(int)(stream & 63)];
                if (chars.Length < 13) return;
                chars[12] = Chars64[(int)((stream >> 6) & 63)];
                if (chars.Length < 14) return;
                chars[13] = Chars64[(int)((stream >> 12) & 63)];
                if (chars.Length < 15) return;
                chars[14] = Chars64[(int)((stream >> 18) & 63)];
                if (chars.Length < 16) return;
                chars[15] = Chars64[(int)((stream >> 24) & 63)];
                if (chars.Length < 17) return;
                chars[16] = Chars64[(int)((stream >> 30) & 63)];
            });
    }
    static int Index(char c)
    {
        int i = Array.IndexOf(Chars64, c);
        return i != -1 ? i : throw new Exception($"Invalid seed char: {c}");
    }
    internal static ulong Parse(string seed, out uint stream)
    {
        stream = (uint)(seed.Length == 12 ? Index(seed[11])
            : seed.Length == 13 ? Index(seed[11]) + (Index(seed[12]) << 6)
            : seed.Length == 14 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12)
            : seed.Length == 15 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18)
            : seed.Length == 16 ? Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18) + (Index(seed[15]) << 24)
            : Index(seed[11]) + (Index(seed[12]) << 6) + (Index(seed[13]) << 12) + (Index(seed[14]) << 18) + (Index(seed[15]) << 24) + (Index(seed[16]) << 30));
        return (((((((((((((((((((
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
            + (ulong)Index(seed[10]);
    }
}