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
            //Dbg.TimeStart();
            Dbg.Regression.Delete();

            for (int i = 0; i < 2; i++)
            {
                Dbg.Info("some info");
                //Dbg.TimeStart("one");
                Dbg.Regression.Add("ONE");
                Dbg.Set("d", 1.23);
                //Dbg.TimeEndStart("two");
                Dbg.CallAdd("cache", () =>
                {
                    Dbg.Regression.Add((double)Dbg.Get("d"));
                    Dbg.Regression.Add("TWO");
                });
                //Dbg.TimeEnd();
                //System.Threading.Thread.Sleep(70000);
                Dbg.Call("cache");
                Dbg.Regression.Add(1.243M);
                Dbg.Output(s => Assert.Equal("Dbg: some info", s));
            }
            Dbg.Regression.Delete();
            //Dbg.Output(writeLine);
        }
    }
}
