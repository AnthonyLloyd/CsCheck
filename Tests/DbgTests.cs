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
            //var time = Dbg.Time();
            Dbg.Regression.Delete();

            for (int i = 0; i < 2; i++)
            {
                Dbg.Info("some info");
                //var t = Dbg.Time("one");
                Dbg.Regression.Add("ONE");
                Dbg.Set("d", 1.23);
                //t = t.EndStart("two");
                Dbg.CallAdd("cache", () =>
                {
                    Dbg.Regression.Add((double)Dbg.Get("d"));
                    Dbg.Regression.Add("TWO");
                });
                //t.End();
                //System.Threading.Thread.Sleep(70000);
                Dbg.Call("cache");
                Dbg.Regression.Add(1.243M);
                //Dbg.Output(s => Assert.Equal("Dbg: some info", s));
            }
            Dbg.Regression.Delete();
            //time.End();
            Dbg.Output(writeLine);
        }
    }
}
