namespace Tests;

using System;
using System.Collections.Generic;
using Xunit;

public class DbgTests
{
    readonly Action<string> writeLine;
    public DbgTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

    static IEnumerable<char> Enumerable(string s) => s;
    static int[] Calc1(double _) => new[] { 1, 2 };
    static double Calc2(int[] _) => 1.2;

    [Fact]
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
        var y = Dbg.Info(s => Enumerable(s), x).DbgCache().DbgInfo();
        Dbg.Regression.Delete();
        Dbg.Output(writeLine);
    }
}