namespace Tests;

using System;
using System.Linq;
using CsCheck;
using Xunit;

public class ErrorMinimisingAllocatorMultiTests
{
    readonly static Gen<double> genDouble =
        Gen.Select(Gen.Int[-100, 100], Gen.Int[-100, 100], Gen.Int[1, 100])
        .Select((a, b, c) => a + (double)b / c);

    readonly static Gen<(long[] Totals, double[] Weights)> genAllocateExample =
        Gen.Select(Gen.Long[-100, 100].Array, genDouble.Array[1, 30])
        .Where((_, weights) => Math.Abs(weights.Sum()) > 1e-9);

    [Fact]
    public void AllocateTotalsCorrectly()
    {
        genAllocateExample.Sample((totals, weights) =>
        {
            var allocations = ErrorMinimisingAllocator.Allocate(totals, weights);
            foreach(var (allocs, total) in allocations.Zip(totals))
                if (allocs.Sum() != total)
                    return false;
            return true;
        });
    }
}
