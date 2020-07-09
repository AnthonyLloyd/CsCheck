using System;
using Xunit;
using CsCheck;

namespace Tests
{
    public class CheckTests
    {
        bool ArrayEqual<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;

        }

        //[Fact]
        public void Version()
        {
            Gen.Select(Gen.Byte, Gen.Byte, Gen.Byte)
            .Select(t => new Version(t.V0, t.V1, t.V2))
            .Array(0, 100)
            .Sample(expected =>
            {
                var actual = (Version[])expected.Clone();
                Array.Reverse(actual);
                return ArrayEqual(expected, actual);
                //Assert.Equal(expected, actual);
            });
        }
    }
}
