using System;
using Xunit;

namespace Tests
{
    public class DbgTests
    {
        readonly Action<string> writeLine;
        public DbgTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

        [Fact]
        public void DbgWalkthrough()
        {
            Dbg.Regression.Delete();

            for (int i = 0; i < 2; i++)
            {
                Dbg.Info("some info");
                Dbg.Regression.Add("ONE");
                var array = new []{ 1, 2 }.Tee(Dbg.Regression.Add);
                Dbg.Set("d", array);
                Dbg.CallAdd("cache", () =>
                {
                    Dbg.Regression.Add((int[])Dbg.Get("d"));
                    Dbg.Regression.Add("TWO");
                });
                Dbg.Call("cache");
                Dbg.Regression.Add(1.243M);
                //Dbg.Output(s => Assert.Equal("Dbg: some info", s));
            }

            Dbg.Regression.Delete();
            Dbg.Output(writeLine);
        }
    }
}
