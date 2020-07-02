using System;
using Xunit;
using CsCheck;

namespace Tests
{
    public class GenTests
    {
        Gen<Version> GenVersion =
            from b1 in Gen.Byte()
            from b2 in Gen.Byte()
            from b3 in Gen.Byte()
            select new Version(b1, b2, b3);

        [Fact]
        public void Test1()
        {
            var x = from b1 in Gen.Byte()
                    from b2 in Gen.Byte()
                    from b3 in Gen.Byte()
                    select new Version(b1, b2, b3);
        }
    }
}
