using Xunit;
using CsCheck;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]

namespace Tests
{
    public class PCGTests
    {
        [Fact] // from the github https://github.com/imneme/pcg-c-basic minimal c implementation http://www.pcg-random.org/download.html#minimal-c-implementation
        public void PCG_Demo_1()
        {
            var pcg = PCG.Parse("36185706b82c2e03f8");
            Assert.Equal(54, pcg.Stream);
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
            var pcg = PCG.Parse("1c04f77d504556f19");
            Assert.Equal(1, pcg.Stream);
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
            var pcg = PCG.Parse("05e64366ec2781f14");
            Assert.Equal(0, pcg.Stream);
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
    }
}
