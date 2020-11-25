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

See the [comparison](Comparison.md) with other random testing libraries.
The low ceremony generators make CsCheck a good choice for C#, but the superior automatic shrinking and performance will make it a good choice for other languages too.

CsCheck also has functionality to make multithreading, performance and regression testing simple and fast.

## Examples

### **1.** Sample test of the range of a unit single. The default sample size is 100.
```csharp
[Fact]
public void Single_Unit_Range()
{
    Gen.Single.Unit.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
}
```

### **2.** Sample test for long ranges.
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

### **3.** Sample one test for int value distribution.
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

### **4.** Sample roundtrip serialization testing.
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

### **5.** Sample Map AddOrUpdate test.
```csharp
static Gen<ImHashMap234<int, int>> GenMap(int upperBound) =>
    Gen.Int[0, upperBound].ArrayUnique.SelectMany(ks =>
        Gen.Int.Array[ks.Length].Select(vs =>
        {
            var m = ImHashMap234<int, int>.Empty;
            for (int i = 0; i < ks.Length; i++)
                m = m.AddOrUpdate(ks[i], vs[i]);
            return m;
        }));

[Fact]
public void AddOrUpdate_Metamorphic()
{
    const int upperBound = 100000;
    Gen.Select(GenMap(upperBound), Gen.Int[0, upperBound], Gen.Int, Gen.Int[0, upperBound], Gen.Int)
    .Sample(t =>
    {
        var map1 = t.V0.AddOrUpdate(t.V1, t.V2).AddOrUpdate(t.V3, t.V4);
        var map2 = t.V1 == t.V3 ? t.V0.AddOrUpdate(t.V3, t.V4)
                                : t.V0.AddOrUpdate(t.V3, t.V4).AddOrUpdate(t.V1, t.V2);
        var seq1 = map1.Enumerate().OrderBy(i => i.Key).Select(i => (i.Key, i.Value));
        var seq2 = map2.Enumerate().OrderBy(i => i.Key).Select(i => (i.Key, i.Value));
        Assert.Equal(seq1, seq2);
    }
    , size: 100_000
    , print: t => t + "\n" + string.Join("\n", t.V0.Enumerate())
    , seed: "42ChASl6qJI5");
}
```

This test compares the items in a Map after two key values are added in opposite order.
It shows the use of configuration parameters including print to override the display of the shrunk sample.

This is an example of a metamorphic test which provides very good coverage of functionality.
More about how useful metamorphic tests can be here: [How to specify it!](https://youtu.be/G0NUOst-53U?t=1639).

```
Failed Tests.IMToolsTests.AddOrUpdate_Metamorphic [45 s]
Error Message:
   CsCheck.CsCheckException : Set seed: "2sGfeoVz3gUh" or $env:CsCheck_Seed = "2sGfeoVz3gUh" to reproduce (5 shrinks, 98,371 skipped, 100,000 total)
Sample: (leaf2{[637]637:591741395; [811]811:49258608}, 42626, 143418877, 70259, -2062982101)
[637]637:591741395
[811]811:49258608
---- Assert.Equal() Failure
Expected: SelectIPartitionIterator<ValueEntry<Int32, Int32>, ValueTuple<Int32, Int32>> [(637, 591741395), (811, 49258608), (42626, 143418877), (70259, -2062982101)]
Actual:   SelectIPartitionIterator<ValueEntry<Int32, Int32>, ValueTuple<Int32, Int32>> [(637, 591741395), (811, 49258608), (42626, 143418877)]
  Stack Trace:
     at CsCheck.Check.Sample[T](Gen`1 gen, Action`1 assert, String seed, Int32 size, Int32 threads, Func`2 print) in C:\Users\Ant\src\CsCheck\CsCheck\Check.cs:line 119
   at Tests.IMToolsTests.AddOrUpdate_Metamorphic() in C:\Users\Ant\src\CsCheck\Tests\IMToolsTests.cs:line 66
----- Inner Stack Trace -----
   at Tests.IMToolsTests.<>c.<AddOrUpdate_Metamorphic>b__5_1(ValueTuple`5 t) in C:\Users\Ant\src\CsCheck\Tests\IMToolsTests.cs:line 70
   at CsCheck.Check.<>c__DisplayClass5_0`1.<Sample>b__0(Int32 _) in C:\Users\Ant\src\CsCheck\CsCheck\Check.cs:line 113
```

For this case there is also a very good Model-based approach. This gives the best coverage but it is not always possible to find a model that is not just a reimplementation.

```csharp
[Fact]
public void AddOrUpdate_ModelBased()
{
    const int upperBound = 100000;
    Gen.Select(GenMap(upperBound), Gen.Int[0, upperBound], Gen.Int)
    .Sample(t =>
    {
        var dic1 = new Dictionary<int, int>();
        foreach (var entry in t.V0.Enumerate()) dic1.Add(entry.Key, entry.Value);
        dic1[t.V1] = t.V2;
        var dic2 = new Dictionary<int, int>();
        foreach (var entry in t.V0.AddOrUpdate(t.V1, t.V2).Enumerate())
            dic2.Add(entry.Key, entry.Value);
        Assert.Equal(dic1, dic2);
    }
    , size: 100_000
    , print: t => t + "\n" + string.Join("\n", t.V0.Enumerate()));
}
```

### **6.** Multithreading test for DictionarySlim. Gen and Action pairs will be run randomly across multiple threads.
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

### **7.** Performance test of linq expressions checking the results are always the same. The first expression is asserted to be faster than the second.
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

### **8.** Performance test of two different ways of multiplying a matrix for a sample of matrix sizes checking the results are always the same.
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

### **9.** Performance test of a new Benchmarks Game submission.
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

### **10.** Performance test of PrefixVarint vs Varint for a given distribution skew.
Repeat is used as the functions are very quick.
```csharp
void PrefixVarint_Faster(double skew)
{
    var bytes = new byte[8];
    Gen.UInt.Skew[skew].Faster(i =>
    {
        int pos = 0;
        ArraySerializer.WritePrefixVarint(bytes, ref pos, i);
        pos = 0;
        return ArraySerializer.ReadPrefixVarint(bytes, ref pos);
    }
    , i =>
    {
        int pos = 0;
        ArraySerializer.WriteVarint(bytes, ref pos, i);
        pos = 0;
        return ArraySerializer.ReadVarint(bytes, ref pos);
    }, threads: 1, sigma: 50, repeat: 10_000)
    .Output(writeLine);
}
[Fact]
public void PrefixVarint_Faster_NoSkew() => PrefixVarint_Faster(0);
[Fact]
public void PrefixVarint_Faster_Skew10() => PrefixVarint_Faster(10);
```

```
Tests.ArraySerializerTests.PrefixVarint_Faster_NoSkew [483ms]
Standard Output Messages:
51.9%[-3..+3] faster, sigma=50.0 (2,539 vs 13)

Tests.ArraySerializerTests.PrefixVarint_Faster_Skew10 [1s 829ms]
Standard Output Messages:
25.5%[-26..+14] faster, sigma=50.0 (8,394 vs 3,046)
```

### **11.** Regression test of portfolio profit and risk.  
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
        h.AddDecimalPlaces(2, portfolio.Positions.Select(p => p.Profit));
        h.AddDecimalPlaces(2, portfolio.Profit(fxRate));
        h.AddDecimalPlaces(2, portfolio.RiskByPosition(fxRate));
    }, 5857230471108592669);
}
```

These tests are in xUnit but could equally be used in any testing framework.

More to see in the [Tests](Tests). There are also 1,000+ F# tests using CsCheck in [MKL.NET](https://github.com/MKL-NET/MKL.NET/tree/master/Tests).

## Configuration

Sample and Faster functions accept configuration optional parameters e.g. size: 100_000, seed: "0N0XIzNsQ0O2", print: t => string.Join(", ", t).

Global defaults can also be set via environment variables.

```powershell
$env:CsCheck_Size = 10000; dotnet test -c Release --filter Multithreading; rm env:CsCheck*

$env:CsCheck_Seed = "0N0XIzNsQ0O2"; dotnet test -c Release --filter List; rm env:CsCheck*

$env:CsCheck_Sigma = 50; dotnet test -c Release -l 'console;verbosity=detailed' --filter Faster; rm env:CsCheck*

$env:CsCheck_Threads = 1; dotnet test -c Release -l 'console;verbosity=detailed' --filter Perf; rm env:CsCheck*
```
