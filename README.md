# CsCheck

<p>
<a href="https://github.com/AnthonyLloyd/CsCheck/actions"><img src="https://github.com/AnthonyLloyd/CsCheck/workflows/CI/badge.svg?branch=master"></a>
<a href="https://www.nuget.org/packages/CsCheck"><img src="https://buildstats.info/nuget/CsCheck?includePreReleases=true"></a>
</p>

CsCheck is a C# random testing library inspired by QuickCheck.

It differs in that generation and shrinking are both based on [PCG](https://www.pcg-random.org), a fast random number generator.

This gives the following advantages:

- Automatic shrinking. Gen classes are composable with no need for Arb classes. So less boilerplate.
- Random testing and shrinking are parallelized. This and PCG make it very fast.
- Shrunk cases have a seed value. Simpler examples can easily be reproduced.
- Shrinking can be repeated to give simpler cases for high dimensional problems.

CsCheck also makes multithreading, performance and regression testing simple and fast.

### Examples

**1.** Sample test of the range of a unit single. The default sample size is 100.
```csharp
[Fact]
public void Single_Unit_Range()
{
    Gen.Single.Unit.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
}
```

**2.** Sample test for chars taken from a string.
```csharp
[Fact]
public void Char_Array()
{
    var chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
    Gen.Char[chars].Sample(c => chars.Contains(c));
}
```

**3.** Sample test for long ranges.
```csharp
[Fact]
public void Long_Range()
{
    (from t in Gen.Select(Gen.Long, Gen.Long)
     let start = Math.Min(t.V0, t.V1)
     let finish = Math.Max(t.V0, t.V1)
     from value in Gen.Long[start, finish]
     select (value, start, finish))
    .Sample(i => Assert.InRange(i.value, i.start, i.finish));
}
```

**4.** Sample test for int value distribution.
```csharp
[Fact]
public void Int_Distribution()
{
    var frequency = 10;
    var buckets = 70;
    var expected = ArrayRepeat(frequency, buckets);
    Gen.Int[0, buckets - 1]
    .Array[frequency * buckets]
    .Select(i => Tally(buckets, i))
    .SampleOne(actual => Check.ChiSquared(expected, actual));
}
```

**5.** Multithreading test for DictionarySlim. Gen and Action pairs will be run randomly across multiple threads.
```csharp
[Fact]
public void Multithreading_DictionarySlim()
{
    var d = new DictionarySlim<int, int>();
    Check.Sample(
        Gen.Int[1, 10],
        i =>
        {
            ref var v = ref d.GetOrAddValueRef(i);
            v = 1 - v;
        },
        Gen.Int[1, 10],
        i =>
        {
            d.TryGetValue(i, out var v);
            Assert.True(v == 0 || v == 1);
        }
    );
}
```

**6.** Performance test of linq expressions checking the results are always the same. The first expression is asserted to be faster than the second.
```csharp
[Fact]
public void Faster_Linq_Random()
{
    Gen.Byte.Array[100, 1000]
    .Faster(
        data => data.Aggregate(0.0, (t, b) => t + b),
        data => data.Select(i => (double)i).Sum()
    )
    .Output(writeLine);
}
```

The performance is raised in an exception if it fails but can also be output if it passes with the above output function.
```
Tests.CheckTests.Faster_Linq_Random [27ms]
Standard Output Messages:
32.2%[-3..+4] faster, sigma=50.0 (2,551 vs 17)
```

 The first number is the estimated median performance improvement with the interquartile range in the square brackets.
 The counts of faster vs slower and the corresponding sigma (the number of standard deviations of the binomial
 distribution for the null hypothosis P(faster) = P(slower) = 0.5) are also shown. The default sigma used is 6.0.

**7.** Performance test of two different ways of multiplying a matrix for a sample of matrix sizes checking the results are always the same.
An external equal assert is used.
```csharp
[Fact]
public void Faster_Matrix_Multiply_Range()
{
    var genDim = Gen.Int[5, 30];
    var genArray = Gen.Double.Unit.Array2D;
    Gen.SelectMany(genDim, genDim, genDim, (i, j, k) =>
        Gen.Select(genArray[i, j], genArray[j, k])
    )
    .Faster(
        t => MulIKJ(t.V0, t.V1),
        t => MulIJK(t.V0, t.V1),
        Assert.Equal
    )
}
```

**8.** Performance test of a new Benchmarks Game submission.
```csharp
[Fact]
public void ReverseComplement_Faster()
{
    if (!File.Exists(Utils.Fasta.Filename)) Utils.Fasta.NotMain(new[] { "25000000" });

    Check.Faster(
        ReverseComplementNew.RevComp.NotMain,
        ReverseComplementOld.RevComp.NotMain,
        threads: 1, timeout: 600_000, sigma: 6
    )
    .Output(writeLine);
}
```

```
Tests.ReverseComplementTests.ReverseComplement_Faster [27s 870ms]
Standard Output Messages:
25.1%[-5..+6] faster, sigma=6.0 (36 vs 0)
```

**9.** Regression test of portfolio profit and risk.
```csharp
[Fact]
public void Portfolio_Small_Mixed_Example()
{
    var portfolio = ModelGen.Portfolio.Example(p =>
           p.Positions.Count == 5
        && p.Positions.Any(p => p.Instrument is Bond)
        && p.Positions.Any(p => p.Instrument is Equity)
    , "0N0XIzNsQ0O2");
    var currencies = portfolio.Positions.Select(p => p.Instrument.Currency).Distinct().ToArray();
    var fxRates = ModelGen.Price.Array[currencies.Length].Example(a =>
        a.All(p => p > 0.75 && p < 1.5)
    , "ftXKwKhS6ec4");
    double fxRate(Currency c) => fxRates[Array.IndexOf(currencies, c)];
    using var hash = Hash.Expected(1408762755);
    hash.Add(portfolio.Positions.Select(p => p.Profit), 2);
    hash.Add(portfolio.Profit(fxRate), 2);
    hash.Add(portfolio.RiskByPosition(fxRate), 2);
}
```
The Example method is used to find, pin and continue to check a suitable generated example e.g. to cover a certain codepath.
Hash will save a cache of a number of results on a successful run and each subsequent run will fail with actual vs expected at the first point of any difference.
Together Example and Hash eliminate the need to commit data files in regression testing while also giving detailed information of any change.

These tests are in xUnit but could equally be used in any testing framework.

More to see in the [Tests](Tests).

### Configuration

Sample and Faster accept configuration parameters. Global defaults can also be set via environment variables.

```powershell
$env:CsCheck_Size = 10000; dotnet test -c Release --filter Multithreading; rm env:CsCheck*

$env:CsCheck_Seed = "0N0XIzNsQ0O2"; dotnet test -c Release --filter List; rm env:CsCheck*

$env:CsCheck_Sigma = 50; dotnet test -c Release -l 'console;verbosity=detailed' --filter Faster; rm env:CsCheck*
```
