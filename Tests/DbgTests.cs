using Xunit;

namespace Tests
{
    public class DbgTests
    {
        [Fact]
        public void Example()
        {
            Dbg.Regression.Delete();
            Dbg.Info("some info");
            Dbg.Regression.Add("ONE");
            Dbg.Set("d", 1.23);
            Dbg.CallDefine("cache", () =>
            {
                Dbg.Regression.Add((double)Dbg.Get("d"));
                Dbg.Regression.Add("TWO");
            });
            Dbg.Call("cache");
            Dbg.Regression.Add(1.243M);
            Dbg.Regression.Dispose();
            Dbg.Output(s => Assert.Equal("Dbg: some info", s));
        }
    }
}
