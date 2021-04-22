using Xunit;

namespace Tests
{
    public class DbgTests
    {
        [Fact]
        public void Example()
        {
            Dbg.Regression.Delete();

            for (int i = 0; i < 2; i++)
            {
                Dbg.Info("some info");
                Dbg.Regression.Add("ONE");
                Dbg.Set("d", 1.23);
                Dbg.CallAdd("cache", () =>
                {
                    Dbg.Regression.Add((double)Dbg.Get("d"));
                    Dbg.Regression.Add("TWO");
                });
                Dbg.Call("cache");
                Dbg.Regression.Add(1.243M);
                Dbg.Output(s => Assert.Equal("Dbg: some info", s));
            }

            Dbg.Regression.Delete();
        }
    }
}
