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
            //Dbg.Time("Example");
            Dbg.Regression.Delete();

            for (int i = 0; i < 2; i++)
            {
                Dbg.Info("some info");
                //Dbg.Time("one");
                Dbg.Regression.Add("ONE");
                Dbg.Set("d", 1.23);
                //Dbg.Time("two");
                Dbg.CallAdd("cache", () =>
                {
                    Dbg.Regression.Add((double)Dbg.Get("d"));
                    Dbg.Regression.Add("TWO");
                });
                //Dbg.TimeEnd();
                Dbg.Call("cache");
                Dbg.Regression.Add(1.243M);
                Dbg.Output(s => Assert.Equal("Dbg: some info", s));
            }
            Dbg.Output(writeLine);
            Dbg.Regression.Delete();
        }
    }
}
