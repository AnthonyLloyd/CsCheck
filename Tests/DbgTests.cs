namespace Tests;

using System.Collections.Generic;

public class DbgTests()
{
    static IEnumerable<char> Enumerable(string s) => s;
    static int[] Calc1(double _) => [1, 2];
    static double Calc2(int[] _) => 1.2;

    [Test]
    public void DbgWalkthrough()
    {
        Dbg.Regression.Delete();

        for (int i = 0; i < 2; i++)
        {
            Dbg.Info("some info");
            var array = Calc1(1.7).DbgTee(Dbg.Regression.Add);
            var d = Calc2(array).DbgSet("d");
            Dbg.CallAdd("cache", () => Dbg.Get("d").DbgInfo("hi"));
        }

        Dbg.Call("cache");
        const string x = "hello";
        var y = Dbg.Info(Enumerable, x).DbgCache().DbgInfo();
        Dbg.Regression.Delete();
        Dbg.Output(TUnitX.WriteLine);
    }
}