using System;
using Xunit;
using CsCheck;

namespace Tests
{
    public class GenTests
    {
        Gen<byte> Bytes = Gen.Byte[0, 100];
        Gen<char> Chars = Gen.Char['a', 'z'];
        [Fact]
        public void Array_Reverse_Reverse_Equal()
        {
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

        bool ArrayEqual<T>(T[] expected, T[] actual)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                if (!expected[i].Equals(actual[i])) return false;
            }
            return true;
        }
    }
}