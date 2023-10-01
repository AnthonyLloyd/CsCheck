namespace Tests;

using CsCheck;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class StableSumTests
{
    [Fact]
    public void Sum()
    {
        Assert.Equal(20000, MathStable.Sum([10000, 1e104, 10000, -1e104]));
        Assert.Equal(1e104, MathStable.Sum([10000, 1e104, 10000]));
        Assert.Equal(1e104, MathStable.Sum([10000, 1e104]));
        Assert.Equal(10000, MathStable.Sum([10000, 1e-104]));
        Assert.Equal(1e-104, MathStable.Sum([10000, 1e-104, -10000]));
        Assert.Equal(-1e104, MathStable.Sum([10000, 1e-104, 10000, -1e104]));
    }

    [Fact]
    public void Shuffle_Check()
    {
        Gen.Double[-100_000_000, 100_000_000, 100_000_000].Array[2, 10]
        .SelectMany(a => Gen.Shuffle(a).Select(s => (a, s)))
        .Sample((original, shuffled) =>
        {
            var originalSum = MathStable.Sum(original);
            var shuffledSum = MathStable.Sum(shuffled);
            return originalSum == shuffledSum;
        });
    }

    //[Fact]
    //public void Sort_Perf()
    //{
    //    Gen.Double[-100_000_000, 100_000_000, 100_000_000].Array[100, 100]
    //    .Faster(
    //        a => MathStable.Sum(a) * 0.0,
    //        a => { a = (double[])a.Clone(); Array.Sort(a, (x, y) => Math.Abs(x).CompareTo(Math.Abs(y))); return a.Sum() * 0.0; }
    //    )
    //    .Output(output.WriteLine);
    //}
}

public static class MathStable
{
    public static double Sum(IEnumerable<double> values)
    {
        var partials = new List<double>();
        foreach (var v in values)
        {
            var hi = v;
            int i = 0;
            for (int j = 0; j < partials.Count; j++)
            {
                var lo = partials[j];
                //if (Math.Abs(hi) < Math.Abs(lo))
                //    (hi, lo) = (lo, hi);
                //var x = hi;
                //hi += lo;
                //lo -= hi - x;
                //(hi, lo) = (hi + lo, lo - (hi + lo - hi));
                (hi, lo) = (hi + lo, Math.Abs(hi) < Math.Abs(lo) ? hi - (lo + hi - lo) : lo - (hi + lo - hi));
                if (lo != 0.0)
                    partials[i++] = lo;
            }
            if (i == partials.Count)
            {
                partials.Add(hi);
            }
            else
            {
                partials[i++] = hi;
                partials.RemoveRange(i, partials.Count - i);
            }
        }
        return partials.Sum();
    }
}