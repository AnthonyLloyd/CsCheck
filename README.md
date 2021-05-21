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
- Shrinking can be continued later to give simpler cases for high dimensional problems.
- Concurrency testing and random shrinking work well together.

See the [comparison](Comparison.md) with other random testing libraries, or how CsCheck does in the [shrinking challenge](https://github.com/jlink/shrinking-challenge).
In one [shrinking challenge test](https://github.com/jlink/shrinking-challenge/blob/main/challenges/binheap.md) CsCheck managed to shrink to a new smaller example than was thought possible and is not reached by any other testing library.
The low ceremony generators make CsCheck a good choice for C#, but the superior automatic shrinking, performance and features will make it a good choice for all .NET languages.

CsCheck also has functionality to make multiple types of testing simple and fast:

- [Random testing](#Random-testing)
- [Model-based testing](#Model-based-testing)
- [Metamorphic testing](#Metamorphic-testing)
- [Concurrency testing](#Concurrency-testing)
- [Causal profiling](#Causal-profiling)
- [Regression testing](#Regression-testing)
- [Performance testing](#Performance-testing)
- [Debug utilities](#Debug-utilities)
- [Configuration](#Configuration)

The following tests are in xUnit but could equally be used in any testing framework.

More to see in the [Tests](Tests). There are also 1,000+ F# tests using CsCheck in [MKL.NET](https://github.com/MKL-NET/MKL.NET/tree/master/Tests).

## Random testing

### Unit Single
```csharp
[Fact]
public void Single_Unit_Range()
{
    Gen.Single.Unit.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
}
```

### Long Range
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

### Int Distribution
```csharp
[Fact]
public void Int_Distribution()
{
    int buckets = 70;
    int frequency = 10;
    int[] expected = ArrayRepeat(buckets, frequency);
    Gen.Int[0, buckets - 1].Array[frequency * buckets]
    .Select(sample => Tally(buckets, sample))
    .SampleOne(actual => Check.ChiSquared(expected, actual));
}
```

### Serialization Roundtrip
```csharp
static void TestRoundtrip<T>(Gen<T> gen, Action<Stream, T> serialize, Func<Stream, T> deserialize)
{
    gen.Sample(t =>
    {
        using var ms = new MemoryStream();
        serialize(ms, t);
        ms.Position = 0;
        return deserialize(ms).Equals(t);
    });
}
[Fact]
public void Varint()
{
    TestRoundtrip(Gen.UInt, StreamSerializer.WriteVarint, StreamSerializer.ReadVarint);
}
[Fact]
public void Double()
{
    TestRoundtrip(Gen.Double, StreamSerializer.WriteDouble, StreamSerializer.ReadDouble);
}
[Fact]
public void DateTime()
{
    TestRoundtrip(Gen.DateTime, StreamSerializer.WriteDateTime, StreamSerializer.ReadDateTime);
}
```

### Shrinking Challenge
```csharp
[Fact]
public void No2_LargeUnionList()
{
    Gen.Int.Array.Array
    .Sample(aa =>
    {
        var hs = new HashSet<int>();
        foreach (var a in aa)
        {
            foreach (var i in a) hs.Add(i);
            if (hs.Count >= 5) return false;
        }
        return true;
    });
}
```

## Model-based testing

Model-based is the most efficient form of random testing.
Only a small amount of code is needed to fully test functionality.
SampleModelBased generates an initial actual and model and then applies a random sequence of operations to both checking that the actual and model are still equal.

### SetSlim Add
```csharp
[Fact]
public void SetSlim_ModelBased()
{
    Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
    .SampleModelBased(
        Gen.Int.Operation<SetSlim<int>, HashSet<int>>((ls, l, i) =>
        {
            ls.Add(i);
            l.Add(i);
        })
        // ... other operations
    );
}
```

## Metamorphic testing

The second most efficient form of random testing is metamorphic which means doing something two different ways and checking they produce the same result.
SampleMetamorphic generates two identical initial samples and then applies the two functions and asserts the results are equal.
This can be needed when no model can be found that is not just a reimplementation.

More about how useful metamorphic tests can be here: [How to specify it!](https://youtu.be/G0NUOst-53U?t=1639).

### MapSlim Update
```csharp
[Fact]
public void MapSlim_Metamorphic()
{
    Gen.Dictionary(Gen.Int, Gen.Byte)
    .Select(d => new MapSlim<int, byte>(d))
    .SampleMetamorphic(
        Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
            (d, t) => { d[t.V0] = t.V1; d[t.V2] = t.V3; },
            (d, t) => { if (t.V0 == t.V2) d[t.V2] = t.V3; else { d[t.V2] = t.V3; d[t.V0] = t.V1; } }
        )
    );
}
```

## Concurrency testing

CsCheck has support for concurrency testing with full shrinking capability.
A concurrent sequence of operations are run on an initial state and the result is compared to all the possible linearized versions.
At least ones of these must be equal to the concurrent version.

Idea from John Hughes [talk](https://youtu.be/1LNEWF8s1hI?t=1603).

### SetSlim
```csharp
[Fact]
public void SetSlim_Concurrency()
{
    Gen.Byte.Array.Select(a => new SetSlim<byte>(a))
    .SampleConcurrent(
        Gen.Byte.Operation<SetSlim<byte>>((l, i) => { lock (l) l.Add(i); }),
        Gen.Int.NonNegative.Operation<SetSlim<byte>>((l, i) => { if (i < l.Count) { var _ = l[i]; } }),
        Gen.Byte.Operation<SetSlim<byte>>((l, i) => { var _ = l.IndexOf(i); }),
        Gen.Operation<SetSlim<byte>>(l => l.ToArray())
    );
}
```

## Causal profiling

Causal profiling is a technique to investigate the effect of speeding up one or more concurrent regions of code.
It shows which regions are the bottleneck and what overall performance gain could be achieved from each region.

Idea from Emery Berger. My blog posts on this [here](http://anthonylloyd.github.io/blog/2019/10/11/causal-profiling).

### Fasta
```csharp
[Fact]
public void Fasta()
{
    Causal.Profile(() => FastaUtils.Fasta.NotMain(10_000_000, null)).Output(writeLine);
}

static int[] Rnds(int i, int j, ref int seed)
{
    var region = Causal.RegionStart("rnds");
    var a = intPool.Rent(BlockSize1);
    var s = a.AsSpan(0, i);
    s[0] = j;
    for (i = 1, j = Width; i < s.Length; i++)
    {
        if (j-- == 0)
        {
            j = Width;
            s[i] = IM * 3 / 2;
        }
        else
        {
            s[i] = seed = (seed * IA + IC) % IM;
        }
    }
    Causal.RegionEnd(region);
    return a;
}
```

## Regression testing

### Portfolio Calculation
**Example** is used to find, pin and continue to check a suitable generated example e.g. to cover a certain codepath.  
**Hash** is used to find and check a hash for a number of results.
It saves a cache of the results on a successful hash check and each subsequent run will fail with actual vs expected at the first point of any difference.  
Together Example and Hash eliminate the need to commit data files in regression testing while also giving detailed information of any change.
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
    Check.Hash(h =>
    {
        h.Add(portfolio.Positions.Select(p => p.Profit));
        h.Add(portfolio.Profit(fxRate));
        h.Add(portfolio.RiskByPosition(fxRate));
    }, 5857230471108592669, decimalPlaces: 2);
}
```

## Performance testing

### Linq Sum
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
32.2%[29.4%..36.5%] faster, sigma=50.0 (2,551 vs 17)
```

 The first number is the estimated median performance improvement with the interquartile range in the square brackets.
 The counts of faster vs slower and the corresponding sigma (the number of standard deviations of the binomial
 distribution for the null hypothosis P(faster) = P(slower) = 0.5) are also shown. The default sigma used is 6.0.

### Matrix Multiply

```csharp
[Fact]
public void Faster_Matrix_Multiply_Range()
{
    var genDim = Gen.Int[5, 30];
    var genArray = Gen.Double.Unit.Array2D;
    Gen.SelectMany(genDim, genDim, genDim, (i, j, k) => Gen.Select(genArray[i, j], genArray[j, k]))
    .Faster(
        MulIKJ,
        MulIJK
    );
}
```

### MapSlim Increment

```csharp
[Fact]
public void MapSlim_Performance_Increment()
{
    Gen.Byte.Array
    .Select(a => (a, new MapSlim<byte, int>(), new Dictionary<int, int>()))
    .Faster(
        (items, mapslim, _) =>
        {
            foreach (var b in items)
                mapslim.GetValueOrNullRef(b)++;
        },
        (items, _, dict) =>
        {
            foreach (var b in items)
            {
                dict.TryGetValue(b, out int c);
                dict[b] = c + 1;
            }
        },
        repeat: 100
    ).Output(writeLine);
}
```

```
Tests.SlimCollectionsTests.MapSlim_Performance_Increment [27 s]
Standard Output Messages:
66.0%[56.4%..74.8%] faster, sigma=200.0 (72,690 vs 13,853)
```

### Benchmarks Game
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
25.1%[20.5%..31.6%] faster, sigma=6.0 (36 vs 0)
```

### Varint
Repeat is used as the functions are very quick.
```csharp
[Fact]
public void Varint_Faster()
{
    Gen.Select(Gen.UInt, Gen.Const(() => new byte[8]))
    .Faster(
        (i, bytes) =>
        {
            int pos = 0;
            ArraySerializer.WriteVarint(bytes, ref pos, i);
            pos = 0;
            return ArraySerializer.ReadVarint(bytes, ref pos);
        },
        (i, bytes) =>
        {
            int pos = 0;
            ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
            pos = 0;
            return ArraySerializer.ReadPrefixVarint(bytes, ref pos);
        }, sigma: 10, repeat: 200)
    .Output(writeLine);
}
```

```
Tests.ArraySerializerTests.Varint_Faster [45 ms]
Standard Output Messages:
10.9%[-3.2%..25.8%] faster, sigma=10.0 (442 vs 190)
```

## Debug utilities

The Dbg module is a set of utilities to collect, count and output debug info, time, classify generators, define and remotely call functions, and perform in code regression during testing.
CsCheck can temporarily be added as a reference to run in non test code.
Note this module is only for temporary debug use and the API may change between minor versions.


### Count, Info, Set, Get, CallAdd, Call
```csharp
public void Normal_Code()
{
    Dbg.Count();
    Dbg.Set("d", 1.23);
    Dbg.Call("helpful");
    Dbg.Info("some info");
    Dbg.CallAdd("test cache", () =>
    {
        Dbg.Info(Dbg.Get("d"));
        Dbg.Info(cacheItems);
    });
}

[Fact]
public void Test()
{
    Dbg.CallAdd("helpful", () =>
    {
        var d = (double)Dbg.Get("d");
        // ...
        Dbg.Set("d", d);
    });
    Normal_Code();
    Dbg.Call("test cache");
    Dbg.Output(writeLine);
}
```

### Regression
```csharp
public double[] Calculation(InputData input)
{
    var part1 = CalcPart1(input);
    // Add items to the regression on first pass, throw/break here if different on subsequent.
    Dbg.Regression.Add(part1);
    var part2 = CalcPart2(part1);
    Dbg.Regression.Add(part2);
    // ...
    var final = CalcFinal(partN);
    Dbg.Regression.Add(final);
    return final;
}

[Fact]
public void Test()
{
    // Remove any previously saved regression data.
    Dbg.Regression.Delete();

    Calculation(InputSource1());

    // End first pass save mode (only needed if second pass is in this process run).
    Dbg.Regression.Close();

    // Subsequent pass could be now or a code change and rerun (without the Delete).
    Calculation(InputSource2());

    // Check full number of items have been reconciled (optional).
    Dbg.Regression.Close();
}
```

### Time
```csharp

public Result CalcPart2(InputData input)
{
    using var time = Dbg.Time();
    // Calc
    time.Line();
    // Calc more
    time.Line();
    // ...
    return result;
}


public void LongProcess()
{
    using var time = Dbg.Time();
    var part1 = CalcPart1(input);
    time.Line();
    var part2 = new List<Result>();
    foreach(var item in part1)
        part2.Add(CalcPart2(item));
    time.Line();
    // ...
    return CalcFinal(partN);
}

[Fact]
public void Test()
{
    LongProcess();
    Dbg.Output(writeLine);
}
```

## Configuration

Check functions accept configuration optional parameters e.g. iter: 100_000, seed: "0N0XIzNsQ0O2", print: t => string.Join(", ", t):

iter - The number of iterations to run in the sample (default 100).  
time - The number of seconds to run the sample.  
seed - The seed to use for the first iteration.  
threads - The number of threads to run the sample on (default number logical CPUs).  
print - A function to convert the state to a string for error reporting (default Check.Print).  
equal - A function to check if the two states are the same (default Check.Equal).  
sigma - For Faster sigma is the number of standard deviations from the null hypothosis (default 6).  
replay - The number of times to retry the seed to reproduce a SampleConcurrent fail (default 100).  

Global defaults can also be set via environment variables:

```powershell
$env:CsCheck_Iter = 10000; dotnet test -c Release --filter Multithreading; rm env:CsCheck*

$env:CsCheck_Time = 60; dotnet test -c Release --filter Multithreading; rm env:CsCheck*

$env:CsCheck_Seed = '0N0XIzNsQ0O2'; dotnet test -c Release --filter List; rm env:CsCheck*

$env:CsCheck_Sigma = 50; dotnet test -c Release -l 'console;verbosity=detailed' --filter Faster; rm env:CsCheck*

$env:CsCheck_Threads = 1; dotnet test -c Release -l 'console;verbosity=detailed' --filter Perf; rm env:CsCheck*
```
