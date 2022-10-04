using System.Linq;
using Xunit;
using CsCheck;
using System;

namespace Tests;

public class AllocateManyTests
{
    readonly Action<string> writeLine;
    public AllocateManyTests(Xunit.Abstractions.ITestOutputHelper output) => writeLine = output.WriteLine;

    [Fact]
    public void Simple()
    {
        var actual = AllocateMany.Allocate(new[] { 9.0, 2.0, 1.0 }, new[] { 1L, 20L, 20L }, new[] { 50.0, 25.0, 25.0 });
        Assert.Equal(new long[][] { new[] { 1L, 0L, 0L }, new[] { 6L, 7L, 7L }, new[] { 14L, 3L, 3L } }, actual);
    }

    [Fact]
    public void Stability()
    {
        Gen.Int[2, 50]
        .SelectMany(n => Gen.Select(Gen.Double[1, 10, 8].ArrayUnique[n], Gen.Long[1, 100].Array[n], Gen.Double[1, 99].Array[2, 7]))
        .Sample((p, q, w) =>
        {
            writeLine($"{q.Length} x {w.Length}");
            writeLine($"q: {string.Join(", ", q)}");
            writeLine($"p: {string.Join(", ", p)}");
            writeLine($"w: {string.Join(", ", w)}");
            var actual = AllocateMany.Allocate(p, q, w);
            writeLine("ans:");
            for (int i = 0; i < q.Length; i++)
            {
                var row = actual[i];
                writeLine(string.Join(", ", row));
            }
            return true;
        }, iter: 1, threads: 1);
    }

    [Fact]
    public void Example_139x19()
    {
        var fills = new (int, int)[] {
            (331,1), (350,2), (357,1), (360,3), (366,2), (371,1), (373,1), (375,3), (376,1), (377,2), (378,2), (379,2), (381,2), (383,3),
            (384,2), (385,5), (386,2), (387,1), (389,3), (390,2), (391,1), (392,6), (393,3), (394,3), (395,10), (396,7), (397,6), (398,2),
            (399,5), (400,3), (401,1), (403,4), (404,4), (405,3), (406,2), (407,3), (408,5), (409,3), (410,2), (411,1), (415,1), (419,7),
            (421,1), (424,2), (425,2), (426,2), (436,3), (437,2), (438,1), (446,1), (447,2)
        };
        Assert.Equal(139, fills.Sum(i => i.Item2));
        Assert.Equal(fills.Select(i => i.Item1), fills.Select(i => i.Item1).Distinct());
        var accounts = new double[] {
            2, 2, 2, 2, 2, 3, 5, 5, 6, 7, 11, 12, 13, 17, 50
        };
        Assert.Equal(139, accounts.Sum());

        //fills = fills.SelectMany(i => Enumerable.Repeat((i.Item1, 1), i.Item2)).ToArray();

        var p = Array.ConvertAll(fills, i => (double)i.Item1);
        var q = Array.ConvertAll(fills, i => (long)i.Item2);
        var actual = AllocateMany.Allocate(p, q, accounts);
    }
}
