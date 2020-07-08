using System.Diagnostics;
using System.Globalization;

namespace CsCheck
{
    public class PCG
    {
        public ulong Inc { get; }
        public ulong State { get; private set; }
        public int Stream => (int)(Inc >> 1);
        PCG(ulong inc, ulong state)
        {
            Inc = inc;
            State = state;
        }
        public PCG(int stream, ulong seed)
        {
            Inc = (ulong)(((long)stream << 1) | 1L);
            State = Inc + seed;
        }
        public PCG(int stream) : this(stream, (ulong)Stopwatch.GetTimestamp()) { }
        public uint Next()
        {
            State = State * 6364136223846793005UL + Inc;
            uint xorshifted = (uint)((State ^ (State >> 18)) >> 27);
            int rot = (int)(State >> 59);
            return (xorshifted >> rot) | (xorshifted << (-rot & 31));
        }
        public ulong Next64() => ((ulong)Next() << 32) + Next();
        public uint Next(uint maxExclusive)
        {
            if (maxExclusive == 1U) return 0U;
            var threshold = ((uint)-(int)maxExclusive) % maxExclusive;
            uint n;
            while ((n = Next()) < threshold) { };
            return n % maxExclusive;
        }
        public ulong Next64(ulong maxExclusive)
        {
            if (maxExclusive <= uint.MaxValue) return Next((uint)maxExclusive);
            var threshold = ((ulong)-(long)maxExclusive) % maxExclusive;
            ulong n;
            while ((n = Next64()) < threshold) { };
            return n % maxExclusive;
        }
        public override string ToString() => (Inc >> 1).ToString("x") + State.ToString("x16");
        public string ToString(ulong state) => (Inc >> 1).ToString("x") + state.ToString("x16");
        public static PCG Parse(string s)
        {
            var stream = uint.Parse(s.Substring(0, s.Length - 16), NumberStyles.HexNumber, null);
            var state = ulong.Parse(s.Substring(s.Length - 16), NumberStyles.HexNumber, null);
            return new PCG((ulong)(((long)stream << 1) | 1L), state);
        }
    }
}