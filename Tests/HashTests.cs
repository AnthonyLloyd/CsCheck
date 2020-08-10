using System;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class HashTests
    {
        [Fact]
        public void Hash_Example()
        {
            var pcg = PCG.Parse("0001cxHI4EF0");
            var hash = new Hash();
            for (int i = 0; i < 100; i++)
            {
                hash.Add(Gen.Bool.Generate(pcg).Item1);
                hash.Add(Gen.SByte.Generate(pcg).Item1);
                hash.Add(Gen.Byte.Generate(pcg).Item1);
                hash.Add(Gen.Short.Generate(pcg).Item1);
                hash.Add(Gen.UShort.Generate(pcg).Item1);
                hash.Add(Gen.Int.Generate(pcg).Item1);
                hash.Add(Gen.UInt.Generate(pcg).Item1);
                hash.Add(Gen.Long.Generate(pcg).Item1);
                hash.Add(Gen.ULong.Generate(pcg).Item1);
                hash.Add(Gen.Float.Generate(pcg).Item1);
                hash.Add(Gen.Double.Generate(pcg).Item1);
                hash.Add(Gen.Decimal.Generate(pcg).Item1);
                hash.Add(Gen.DateTime.Generate(pcg).Item1);
                hash.Add(Gen.TimeSpan.Generate(pcg).Item1);
                hash.Add(Gen.DateTimeOffset.Generate(pcg).Item1);
                hash.Add(Gen.Guid.Generate(pcg).Item1);
                hash.Add(Gen.Char.Generate(pcg).Item1);
                hash.Add(Gen.String.Generate(pcg).Item1);
            }
            Assert.Equal(-918152667, hash.ToHashCode());
        }

        [Fact]
        public void HashStream_Parts()
        {
            Gen.Byte.Array[0, 31].Array[3, 10]
            .Sample(bs =>
            {
                var actual = new HashStream();
                actual.Write(bs[0]);
                var expected = new HashStream();
                expected.Write(bs[0]);
                for (int i = 1; i < bs.Length; i++) actual.Write(bs[i]);
                expected.Write(bs.Skip(1).SelectMany(i => i).ToArray());
                Assert.Equal(actual.ToHashCode(), expected.ToHashCode());
            });
        }
    }
}