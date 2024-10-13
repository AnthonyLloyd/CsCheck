namespace Tests;

using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CsCheck;
using Perfolizer.Metrology;
using Xunit;

public class PCGTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact] // from the github https://github.com/imneme/pcg-c-basic minimal c implementation http://www.pcg-random.org/download.html#minimal-c-implementation
    public void PCG_Demo_1()
    {
        var pcg = PCG.Parse("1xn1HwIbwfUS");
        Assert.Equal(54U, pcg.Stream);
        Assert.Equal(0x185706b82c2e03f8UL, pcg.State);
        Assert.Equal(0x7b47f409u, pcg.Next());
        Assert.Equal(0x2b47fed88766bb05UL, pcg.State);
        Assert.Equal(0xba1d3330u, pcg.Next());
        Assert.Equal(0x8b33296d19bf5b4eUL, pcg.State);
        Assert.Equal(0x83d2f293u, pcg.Next());
        Assert.Equal(0xf7079824c154bf23UL, pcg.State);
        Assert.Equal(0xbfa4784bu, pcg.Next());
        Assert.Equal(0xebbf9e97aa16f694UL, pcg.State);
        Assert.Equal(0xcbed606eu, pcg.Next());
        Assert.Equal(0x8303569fbe80c471UL, pcg.State);
        Assert.Equal(0xbfc6a3adu, pcg.Next());
        Assert.Equal(0xbeb6d0b73fdb974aUL, pcg.State);
        Assert.Equal(0x812fff6du, pcg.Next());
        Assert.Equal(0xed81149f2fb94e6fUL, pcg.State);
        Assert.Equal(0xe61f305au, pcg.Next());
        Assert.Equal(0x730f84eec16daf0UL, pcg.State);
        Assert.Equal(0xf9384b90u, pcg.Next());
        Assert.Equal(0x91723b7b84518c9dUL, pcg.State);
    }

    [Fact]
    public void PCG_Demo_2()
    {
        var pcg = PCG.Parse("c1ftZk4lmYp1");
        Assert.Equal(1U, pcg.Stream);
        Assert.Equal(0xc04f77d504556f19UL, pcg.State);
        Assert.Equal(0x0d01e424u, pcg.Next());
        Assert.Equal(0x2680fbb23aaeee68UL, pcg.State);
        Assert.Equal(0xeb1929a2u, pcg.Next());
        Assert.Equal(0x6494c850bb8d804bUL, pcg.State);
        Assert.Equal(0x00428cebu, pcg.Next());
        Assert.Equal(0x421477dd1a2bc232UL, pcg.State);
        Assert.Equal(0x747f0a17u, pcg.Next());
        Assert.Equal(0x1d1fc5c22e21f0cdUL, pcg.State);
        Assert.Equal(0xe4a907efu, pcg.Next());
        Assert.Equal(0xbfbfbd4bf5be070cUL, pcg.State);
        Assert.Equal(0x686c869fu, pcg.Next());
        Assert.Equal(0x39b2141121e2311fUL, pcg.State);
        Assert.Equal(0xab4acaedu, pcg.Next());
        Assert.Equal(0xfead1480f62c0376UL, pcg.State);
        Assert.Equal(0x0bfa48c7u, pcg.Next());
        Assert.Equal(0xbb1c012e272225c1UL, pcg.State);
        Assert.Equal(0x15469766u, pcg.Next());
        Assert.Equal(0xe30a9b89171061f0UL, pcg.State);
    }

    [Fact]
    public void PCG_Demo_3()
    {
        var pcg = PCG.Parse("5VAdCX2u1Yk0");
        Assert.Equal(0U, pcg.Stream);
        Assert.Equal(0x5e64366ec2781f14UL, pcg.State);
        Assert.Equal(0x361c3e74u, pcg.Next());
        Assert.Equal(0x40e1e399cd2c6285UL, pcg.State);
        Assert.Equal(0x532acb4fu, pcg.Next());
        Assert.Equal(0xdbd4fc47e9164c62UL, pcg.State);
        Assert.Equal(0x3bfccb00u, pcg.Next());
        Assert.Equal(0x9160232795da0b3bUL, pcg.State);
        Assert.Equal(0x46d6c872u, pcg.Next());
        Assert.Equal(0x7590f9e9903d3e60UL, pcg.State);
        Assert.Equal(0x454e4b43u, pcg.Next());
        Assert.Equal(0xd8d165a68a9596e1UL, pcg.State);
        Assert.Equal(0xbf263a6au, pcg.Next());
        Assert.Equal(0x9e9a886f2f1a248eUL, pcg.State);
        Assert.Equal(0x7cae8e93u, pcg.Next());
        Assert.Equal(0x88e915f0ae60def7UL, pcg.State);
        Assert.Equal(0x5c2d9c24u, pcg.Next());
        Assert.Equal(0x5b671fcecf66ba6cUL, pcg.State);
        Assert.Equal(0xf0b0f70cu, pcg.Next());
        Assert.Equal(0x9dc31b5cfc6658fdUL, pcg.State);
    }

    [Fact]
    public void PCG_Bound_UInt()
    {
        Gen.UInt.Sample(i =>
        {
            if (i == 0U) return; // as Next(0) is an error
            uint threshold = (uint)-(int)i % i;
            Assert.Equal(threshold, (uint.MaxValue % i + 1U) % i);
        });
    }

    [Fact]
    public void PCG_Bound_ULong()
    {
        Gen.ULong.Sample(i =>
        {
            if (i == 0UL) return; // as Next64(0) is an error
            ulong threshold = (ulong)-(long)i % i;
            Assert.Equal(threshold, (ulong.MaxValue % i + 1UL) % i);
        });
    }

    readonly Gen<PCG> genPCG =
        Gen.Select(Gen.UInt, Gen.ULong,
            (stream, seed) => new PCG(stream, seed));

    [Fact]
    public void PCG_Next()
    {
        genPCG
        .Select(i => i.Next())
        .Array[20]
        .Sample(t =>
        {
            var expected = Enumerable.Repeat(10, 32).ToArray();
            var actual = new int[32];
            foreach (var i in t)
            {
                var mask = 1U;
                for (int m = 0; m < 32; m++)
                {
                    if ((i & mask) == mask) actual[m]++;
                    mask <<= 1;
                }
            }
            Check.ChiSquared(expected, actual, 10);
        }, iter: 1);
    }

    [Fact]
    public void PCG_Next64()
    {
        genPCG
        .Select(i => i.Next64())
        .Array[20]
        .Sample(t =>
        {
            var expected = Enumerable.Repeat(10, 64).ToArray();
            var actual = new int[64];
            foreach (var i in t)
            {
                var mask = 1UL;
                for (int m = 0; m < 64; m++)
                {
                    if ((i & mask) == mask) actual[m]++;
                    mask <<= 1;
                }
            }
            Check.ChiSquared(expected, actual, 10);
        }, iter: 1);
    }

    [Fact]
    public void PCG_Next_UInt()
    {
        Gen.UInt[1, uint.MaxValue].Select(genPCG)
        .Select((max, pcg) => (max, pcg.Next(max)))
        .Sample((max, x) => x <= max);
    }

    [Fact]
    public void PCG_Next64_ULong()
    {
        Gen.ULong[1, ulong.MaxValue].Select(genPCG)
        .Select((max, pcg) => (max, pcg.Next64(max)))
        .Sample((max, x) => x <= max);
    }

    [Fact]
    public void SeedString_RoundTrip()
    {
        Gen.Select(Gen.ULong, Gen.UInt)
        .Sample((state, stream) =>
        {
            var seed = SeedString.ToString(state, stream);
            var state2 = SeedString.Parse(seed, out var stream2);
            Assert.Equal((state, stream), (state2, stream2));
        });
    }

    [Fact]
    public void PCG_ToString_Roundtrip()
    {
        genPCG.Sample(expected =>
        {
            var actual = PCG.Parse(expected.ToString());
            return expected.Stream == actual.Stream
                && expected.State == actual.State;
        });
    }

    [Fact]
    public void Double_Exp_Bug()
    {
        const double root2 = -6.3E-102;
        const double root3 = 6.6854976605820742;
        Gen.Double[root2, root3 * 2.0].Sample(_ => true);
    }

    [Fact]
    public void PCG_Multiplier_Is_Not_Faster()
    {
        Gen.Select(Gen.UInt, Gen.ULong, Gen.UInt[2, 10_000])
        .Select((i, s, m) => (new PCG(i, s), new PCGTest(i, s), m, HashHelper.GetFastModMultiplier(m)))
        .Faster(
            (_, n, u, m) => n.Next(u, m),
            (o, _, u, _) => o.Next(u),
            repeat: 1000,
            raiseexception: false,
            writeLine: output.WriteLine
        );
    }

    static void Ignore<T>(T _) { }

    [Fact]
    public void PCG_New_Is_Not_Faster()
    {
        Gen.Select(Gen.UInt, Gen.ULong, Gen.UInt[2, 10_000])
        .Select((i, s, m) => (new PCG(i, s), new PCGTest(i, s), m))
        .Faster(
            (_, n, m) => Ignore(n.Next(m)),
            (o, _, m) => Ignore(o.Next(m)),
            repeat: 1000,
            raiseexception: false,
            writeLine: output.WriteLine
        );
    }

    [Fact]
    public void PCG_Lemire_Is_Faster()
    {
        Gen.Select(Gen.UInt, Gen.ULong, Gen.UInt[2, 10_000])
        .Select((i, s, m) => (new PCG(i, s), new PCGTest(i, s), m, ((ulong)-m) % m))
        .Faster(
            (_, n, m, t) => Ignore(n.NextLemire(m, t)),
            (o, _, m, _) => Ignore(o.Next(m)),
            repeat: 1000,
            raiseexception: false,
            writeLine: output.WriteLine
        );
    }

    [Fact]
    public void PCG_Current_With_Threshold_Is_Faster()
    {
        Gen.Select(Gen.UInt, Gen.ULong, Gen.UInt[2, 10_000])
        .Select((i, s, m) => (new PCGTest(i, s), new PCGTest(i, s), m, ((uint)-(int)m) % m))
        .Faster(
            (_, n, m, t) => Ignore(n.NextLemire(m, t)),
            (o, _, m, t) => Ignore(o.NextCurrentWithThreshold(m, t)),
            repeat: 1000,
            raiseexception: false,
            writeLine: output.WriteLine
        );
    }

    public sealed class PCGTest
    {
        static int threadCount;
        [ThreadStatic] static PCG? threadPCG;
        public static PCG ThreadPCG => threadPCG ??= new PCG((uint)Interlocked.Increment(ref threadCount));
        readonly ulong Inc;
        public ulong State;
        public uint Stream => (uint)(Inc >> 1);
        public ulong Seed => State - Inc;
        PCGTest(ulong inc, ulong state)
        {
            Inc = inc;
            State = state;
        }
        public PCGTest(uint stream, ulong seed)
        {
            Inc = (stream << 1) | 1UL;
            State = Inc + seed;
        }
        public PCGTest(uint stream) : this(stream, (ulong)Stopwatch.GetTimestamp()) { }
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
            var x = Next();
            var m = (ulong)x * maxExclusive;
            if ((uint)m < maxExclusive)
            {
                var t = (uint)-maxExclusive;
                if (t >= maxExclusive)
                {
                    t -= maxExclusive;
                    if (t >= maxExclusive)
                        t %= maxExclusive;
                }
                while ((uint)m < t)
                {
                    x = Next();
                    m = (ulong)x * maxExclusive;
                }
            }
            return (uint)(m >> 32);
        }
        public uint NextLemire(uint maxExclusive, ulong threshold)
        {
            ulong m;
            do
            {
                m = (ulong)Next() * maxExclusive;
            }
            while ((uint)m < threshold);
            return (uint)(m >> 32);
        }
        public uint NextCurrentWithThreshold(uint maxExclusive, uint threshold)
        {
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
        public static PCGTest Parse(string seed)
        {
            var state = SeedString.Parse(seed, out var stream);
            return new PCGTest((stream << 1) | 1UL, state);
        }
    }
}