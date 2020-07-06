using System;
using Xunit;
using CsCheck;

namespace Tests
{
    public class GenTests
    {
        [Fact]
        public void Int()
        {
            Gen.Int
            .SelectMany(i => Gen.Int[0, int.MaxValue - i], (s, l) => (s, f: s + l - 1))
            .SelectMany(r => Gen.Int[r.s, r.f], (r, i) => (r.s, r.f, i))
            .Assert(i => Assert.InRange(i.i, i.s, i.f));
        }

        [Fact]
        public void Float()
        {
            Gen.Float.Assert(f => Assert.InRange(f, -100f, 100f));
        }

        [Fact]
        public void Double()
        {
            Gen.Double.Assert(f => Assert.InRange(f, -100.0, 100.0));
        }

        [Fact]
        public void Array_Reverse_Reverse_Equal()
        {
            static bool ArrayEqual<T>(T[] expected, T[] actual)
            {
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!expected[i].Equals(actual[i])) return false;
                }
                return true;
            }

            Gen.Select(Gen.Byte, Gen.Byte, Gen.Byte, (a, b, c) => new Version(a, b, c))
            .Array(0, 100)
            .Assert(expected =>
            {
                var actual = (Version[])expected.Clone();
                Array.Reverse(actual);
                Array.Reverse(actual);
                return ArrayEqual(expected, actual);
            });
        }
    }
}