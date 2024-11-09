# CsCheck
[![build](https://github.com/AnthonyLloyd/CsCheck/workflows/CI/badge.svg?branch=master)](https://github.com/AnthonyLloyd/CsCheck/actions) [![Nuget](https://img.shields.io/nuget/v/CsCheck.svg)](https://www.nuget.org/packages/CsCheck/)


CsCheck is a C# random testing library inspired by QuickCheck.

It differs in that generation and shrinking are both based on [PCG](https://www.pcg-random.org), a fast random number generator.

This gives the following advantages over tree based shrinking libraries:

- Automatic shrinking. Gen classes are composable with no need for Arb classes. So less boilerplate.
- Random testing and shrinking are parallelized. This and PCG make it very fast.
- Shrunk cases have a seed value. Simpler examples can easily be reproduced.
- Shrinking can be continued later to give simpler cases for high dimensional problems.
- Parallel testing and random shrinking work well together. Repeat is not needed.

See [why](https://github.com/AnthonyLloyd/CsCheck/blob/master/Why.md) you should use it, the [comparison](https://github.com/AnthonyLloyd/CsCheck/blob/master/Comparison.md) with other random testing libraries, or how CsCheck does in the [shrinking challenge](https://github.com/jlink/shrinking-challenge).
In one [shrinking challenge test](https://github.com/jlink/shrinking-challenge/blob/main/challenges/binheap.md) CsCheck managed to shrink to a new smaller example than was thought possible and is not reached by any other testing library.
CsCheck is the only random testing library that can always shrink to the simplest example (given enough time).

CsCheck also has functionality to make multiple types of testing simple and fast:

- [Random testing](#Random-testing)
- [Model-based testing](#Model-based-testing)
- [Metamorphic testing](#Metamorphic-testing)
- [Parallel testing](#Parallel-testing)
- [Causal profiling](#Causal-profiling)
- [Regression testing](#Regression-testing)
- [Performance testing](#Performance-testing)
- [Debug utilities](#Debug-utilities)
- [Configuration](#Configuration)
- [Development](#Development)

The following tests are in xUnit but could equally be used in any testing framework.

More to see in the [Tests](https://github.com/AnthonyLloyd/CsCheck/tree/master/Tests). There are also 1,000+ F# tests using CsCheck in [MKL.NET](https://github.com/MKL-NET/MKL.NET/tree/master/Tests).

No Reflection was used in the making of this product.

## Generator Creation Example

Use **Gen** and its Linq methods to compose generators for any type. Here we create a **Gen** for json documents.
More often it will simply be composing a few primitives and collections.
Don't worry about shrinking as it's automatic and the best in the business.

```csharp
static readonly Gen<string> genString = Gen.String[Gen.Char.AlphaNumeric, 2, 5];
static readonly Gen<JsonNode> genJsonValue = Gen.OneOf<JsonNode>(
    Gen.Bool.Select(x => JsonValue.Create(x)),
    Gen.Byte.Select(x => JsonValue.Create(x)),
    Gen.Char.AlphaNumeric.Select(x => JsonValue.Create(x)),
    Gen.DateTime.Select(x => JsonValue.Create(x)),
    Gen.DateTimeOffset.Select(x => JsonValue.Create(x)),
    Gen.Decimal.Select(x => JsonValue.Create(x)),
    Gen.Double.Select(x => JsonValue.Create(x)),
    Gen.Float.Select(x => JsonValue.Create(x)),
    Gen.Guid.Select(x => JsonValue.Create(x)),
    Gen.Int.Select(x => JsonValue.Create(x)),
    Gen.Long.Select(x => JsonValue.Create(x)),
    Gen.SByte.Select(x => JsonValue.Create(x)),
    Gen.Short.Select(x => JsonValue.Create(x)),
    genString.Select(x => JsonValue.Create(x)),
    Gen.UInt.Select(x => JsonValue.Create(x)),
    Gen.ULong.Select(x => JsonValue.Create(x)),
    Gen.UShort.Select(x => JsonValue.Create(x)));
static readonly Gen<JsonNode> genJsonNode = Gen.Recursive<JsonNode>((depth, genJsonNode) =>
{
    if (depth == 5) return genJsonValue;
    var genJsonObject = Gen.Dictionary(genString, genJsonNode.Null())[0, 5].Select(d => new JsonObject(d));
    var genJsonArray = genJsonNode.Null().Array[0, 5].Select(i => new JsonArray(i));
    return Gen.OneOf(genJsonObject, genJsonArray, genJsonValue);
});
```

## Random testing

**Sample** is used to perform tests with a generator. Either return false or throw an exception for failure.
**Sample** will aggressively shrink any failure down to the simplest example.  
The default sample size is 100 iterations. Set iter: to change this or time: to run for a number of seconds.  
Setting these from the command line can be a good way to run your tests in different ways and in Release mode.

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
    int[] expected = Enumerable.Repeat(frequency, buckets).ToArray();
    Gen.Int[0, buckets - 1].Array[frequency * buckets]
    .Select(sample => Tally(buckets, sample))
    .Sample(actual => Check.ChiSquared(expected, actual));
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

### Recursive
```csharp
record MyObj(int Id, MyObj[] Children);

[Fact]
public void RecursiveDepth()
{
    int maxDepth = 4;
    Gen.Recursive<MyObj>((i, my) =>
        Gen.Select(Gen.Int, my.Array[0, i < maxDepth ? 6 : 0], (i, a) => new MyObj(i, a))
    )
    .Sample(i =>
    {
        static int Depth(MyObj o) => o.Children.Length == 0 ? 0 : 1 + o.Children.Max(Depth);
        return Depth(i) <= maxDepth;
    });
}
```

### Classify

Change the return in **Sample** to a string to produce a summary classification table.
All other optional parameters work the same but writeLine: is now mandatory.

```csharp
[Fact]
public void AllocatorMany_Classify()
{
    Gen.Select(Gen.Int[3, 30], Gen.Int[3, 15]).SelectMany((rows, cols) =>
        Gen.Select(
            Gen.Int[0, 5].Array[cols].Where(a => a.Sum() > 0).Array[rows],
            Gen.Int[900, 1000].Array[rows],
            Gen.Int.Uniform))
    .Sample((solution,
             rowPrice,
             seed) =>
    {
        var rowTotal = Array.ConvertAll(solution, row => row.Sum());
        var colTotal = Enumerable.Range(0, solution[0].Length).Select(col => solution.SumCol(col)).ToArray();
        var allocation = AllocatorMany.Allocate(rowPrice, rowTotal, colTotal, new(seed), time: 60);
        if (!TotalsCorrectly(rowTotal, colTotal, allocation.Solution))
            throw new Exception("Does not total correctly");
        return $"{(allocation.KnownGlobal ? "Global" : "Local")}/{allocation.SolutionType}";
    }, output.WriteLine, time: 900);
}
```

|                    | Count |       % |      Median |     Lower Q |     Upper Q |     Minimum |     Maximum |
|--------------------|------:|--------:|------------:|------------:|------------:|------------:|------------:|
| Global             |   458 |  50.22% |             |             |             |             |             |
|   RoundingMinimum  |   343 |  37.61% |      2.68ms |      0.50ms |     10.85ms |      0.03ms |    190.92ms |
|   EveryCombination |    87 |   9.54% |    173.99ms |     16.80ms |  1,199.64ms |      0.20ms | 42,257.35ms |
|   RandomChange     |    28 |   3.07% | 59,592.98ms | 55,267.94ms | 59,901.58ms | 38,575.41ms | 60,107.64ms |
| Local              |   454 |  49.78% |             |             |             |             |             |
|   RoundingMinimum  |   301 |  33.00% | 60,000.12ms | 60,000.04ms | 60,003.70ms | 60,000.02ms | 60,144.84ms |
|   RandomChange     |    90 |   9.87% | 60,000.06ms | 60,000.03ms | 60,004.41ms | 60,000.02ms | 60,136.59ms |
|   EveryCombination |    63 |   6.91% | 60,000.10ms | 60,000.03ms | 60,001.29ms | 60,000.01ms | 60,019.36ms |

## Model-based testing

Model-based is the most efficient form of random testing.
Only a small amount of code is needed to fully test functionality.
SampleModelBased generates an initial actual and model and then applies a random sequence of operations to both checking that the actual and model are still equal.

### SetSlim Add
```csharp
[Fact]
public void
SetSlim_ModelBased()
{
    Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
    .SampleModelBased(
        Gen.Int.Operation<SetSlim<int>, HashSet<int>>(
            (ss, i) => ss.Add(i),
            (hs, i) => hs.Add(i)
        )
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

## Parallel testing

CsCheck has support for parallel testing with full shrinking capability.
A number of operations are run sequentially and then a number in parallel on an initial state and the result is compared to all the possible linearized versions.
At least one of these must be equal to the parallel result.

Idea from John Hughes [talk](https://youtu.be/1LNEWF8s1hI?t=1603) and [paper](https://github.com/AnthonyLloyd/AnthonyLloyd.github.io/raw/master/public/cscheck/finding-race-conditions.pdf). This is easier to implement with CsCheck than QuickCheck because the random shrinking does not need to repeat each step as QuickCheck does (10 times by default) to make shrinking deterministic.

```csharp
[Fact]
public void SampleParallel_ConcurrentQueue()
{
    Gen.Const(() => new ConcurrentQueue<int>())
    .SampleParallel(
        Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
        Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _))
    );
}
```

Can also be tested against a model (which doesn't need to be thread-safe):

```csharp
[Fact]
public void SampleParallelModel_ConcurrentQueue()
{
    Gen.Const(() => (new ConcurrentQueue<int>(), new Queue<int>()))
    .SampleParallel(
        Gen.Int.Operation<ConcurrentQueue<int>, Queue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i), (q, i) => q.Enqueue(i)),
        Gen.Operation<ConcurrentQueue<int>, Queue<int>>("TryDequeue()", q => q.TryDequeue(out _), q => q.TryDequeue(out _))
    );
}
```

## Causal profiling

Causal profiling is a technique to investigate the effect of speeding up one or more concurrent regions of code.
It shows which regions are the bottleneck and what overall performance gain could be achieved from each region.

Idea from Emery Berger. My blog posts on this [here](http://anthonylloyd.github.io/blog/2019/10/11/causal-profiling).

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
**Single** is used to find, pin and continue to check a suitable generated example e.g. to cover a certain codepath.  
**Hash** is used to find and check a hash for a number of results.  
It saves a temp cache of the results on a successful hash check and each subsequent run will fail with actual vs expected at the first point of any difference.  
Together **Single** and **Hash** eliminate the need to commit data files in regression testing while also giving detailed information of any change.

```csharp
[Fact]
public void Portfolio_Small_Mixed_Example()
{
    var portfolio = ModelGen.Portfolio.Single(p =>
           p.Positions.Count == 5
        && p.Positions.Any(p => p.Instrument is Bond)
        && p.Positions.Any(p => p.Instrument is Equity)
    , "0N0XIzNsQ0O2");
    var currencies = portfolio.Positions.Select(p => p.Instrument.Currency).Distinct().ToArray();
    var fxRates = ModelGen.Price.Array[currencies.Length].Single(a =>
        a.All(p => pp is > 0.75 and < 1.5)
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

**Faster** is used to statistically test that the first method is faster than the second and some condition is satisfied (by default equality of the output of the two methods).  
Since it's statistical and relative you can run it as a normal test anywhere e.g. across multiple platforms on a continuous integration server.  
It's fast because it runs in parallel and knows when to stop.
It's just what you need to iteratively improve performance while making sure it still produces the correct results.

```csharp
[Fact]
public void Faster_Linq_Random()
{
    Gen.Byte.Array[100, 1000]
    .Faster(
        data => data.Aggregate(0.0, (t, b) => t + b),
        data => data.Select(i => (double)i).Sum(),
        writeLine: output.WriteLine
    );
}
```

The performance is raised in an exception if it fails but can also be output if it passes with the above output function.
```
Tests.CheckTests.Faster_Linq_Random [27ms]
Standard Output Messages:
32.29%[29.47%..36.51%] 1.48x[1.42x..1.58x] faster, sigma=50.0 (2,551 vs 17)
```

 The first number is the estimated percentage median performance improvement with the interquartile range in the square brackets.
 The second number is the estimated times median performance improvement with the interquartile range in the square brackets.
 33⅓% faster = 1.5x faster and 90% faster = 10x faster take your pick.
 The counts of faster vs slower and the corresponding sigma (the number of standard deviations of the binomial
 distribution for the null hypothesis P(faster) = P(slower) = 0.5) are also shown. The default sigma used is 6.0.

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
        repeat: 100,
        writeLine: output.WriteLine);
}
```

```
Tests.SlimCollectionsTests.MapSlim_Performance_Increment [27 s]
Standard Output Messages:
66.02%[56.48%..74.81%] 2.94x[2.30x..3.97x] faster, sigma=200.0 (72,690 vs 13,853)
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
        writeLine: output.WriteLine);
}
```

```
Tests.ReverseComplementTests.ReverseComplement_Faster [27s 870ms]
Standard Output Messages:
25.15%[20.58%..31.60%] 1.34x[1.26x..1.46x] faster, sigma=6.0 (36 vs 0)
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
        }, sigma: 10, repeat: 200, writeLine: output.WriteLine);
}
```

```
Tests.ArraySerializerTests.Varint_Faster [45 ms]
Standard Output Messages:
10.94%[-3.27%..25.81%] 1.12x[0.97x..1.35x] faster, sigma=10.0 (442 vs 190)
```

## Debug utilities

The Dbg module is a set of utilities to collect, count and output debug info, time, classify generators, define and remotely call functions, and perform in code regression during testing.
CsCheck can temporarily be added as a reference to run in non test code.
Note this module is only for temporary debug use and the API may change between minor versions.


### Count, Info, Set, Get, CallAdd, Call
```csharp
public void Normal_Code(int z)
{
    Dbg.Count();
    var d = Calc1(z).DbgSet("d");
    Dbg.Call("helpful");
    var c = Calc2(d).DbgInfo("c");
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
    Normal_Code(z);
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
    var part2 = CalcPart2(part1).DbgTee(Dbg.Regression.Add); // Tee can be used to do this inline.
    // ...
    return CalcFinal(partN).DbgTee(Dbg.Regression.Add);
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

## Logging

CsCheck now supports logging types and pass and fail results for analysis in Sample. We include a Tyche logging implementation.

## Configuration

Check functions accept configuration optional parameters e.g. iter: 100_000, seed: "0N0XIzNsQ0O2", print: t => string.Join(", ", t):

iter - The number of iterations to run in the sample (default 100).  
time - The number of seconds to run the sample.  
seed - The seed to use for the first iteration.  
threads - The number of threads to run the sample on (default number logical CPUs).  
timeout - The timeout in seconds to use for Faster (default 60 seconds).  
print - A function to convert the state to a string for error reporting (default Check.Print).  
equal - A function to check if the two states are the same (default Check.Equal).  
sigma - For Faster sigma is the number of standard deviations from the null hypothesis (default 6).  
replay - The number of times to retry the seed to reproduce a SampleParallel fail (default 100).  

Global defaults can also be set via environment variables:

```powershell
dotnet test -c Release -e CsCheck_Iter=10000 --filter Multithreading

dotnet test -c Release -e CsCheck_Time=60 --filter Multithreading

dotnet test -c Release -e CsCheck_Seed=0N0XIzNsQ0O2 --filter List

dotnet test -c Release -e CsCheck_Sigma=50 -l 'console;verbosity=detailed' --filter Faster

dotnet test -c Release -e CsCheck_Threads=1 -l 'console;verbosity=detailed' --filter Perf
```

## Development

Contributions are very welcome!

CsCheck was designed to be easily extended. If you have created a cool `Gen` or extension, please consider a PR.

Apache 2 and free forever.