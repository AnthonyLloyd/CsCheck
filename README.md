# CsCheck

<p>
<a href="https://github.com/AnthonyLloyd/CsCheck/actions"><img src="https://github.com/AnthonyLloyd/CsCheck/workflows/CI/badge.svg?branch=master"></a>
<a href="https://www.nuget.org/packages/CsCheck"><img src="https://buildstats.info/nuget/CsCheck?includePreReleases=true"></a>
</p>

CsCheck is a C# random and performance testing library inspired by QuickCheck.

It differs in that generation and shrinking are both based on [PCG](https://www.pcg-random.org), a fast random number generator.

This gives the following advantages:

- Automatic shrinking. Gen classes are composable with no need for Arb classes. So less boilerplate.
- Random testing and shrinking can run in parallel. This and PCG make it very fast.
- Shrunk cases report the seed so they can be rerun. Simpler examples can easily be reproduced.
- Shrinking can be repeated to give simpler cases for high dimensional problems.

### Examples

Sample test of the range of a unit single.
```csharp
[Fact]
public void Single_Unit_Range()
{
    Gen.Single.Unit.Sample(f => Assert.InRange(f, 0f, 0.9999999f));
}
```

Sample test for chars taken from a string.
```csharp
[Fact]
public void Char_Array()
{
    var chars = "abcdefghijklmopqrstuvwxyz0123456789_/";
    Gen.Char[chars].Sample(c => Assert.True(chars.Contains(c)));
}
```

Sample test for long ranges.
```csharp
[Fact]
public void Long_Range()
{
    (from t in Gen.Long.Select(Gen.Long)
     let start = Math.Min(t.V0, t.V1)
     let finish = Math.Max(t.V0, t.V1)
     from value in Gen.Long[start, finish]
     select (value, start, finish))
    .Sample(i => Assert.InRange(i.value, i.start, i.finish));
}
```

Performance test of two different ways of multiplying a matrix for a range of matrix sizes.
```csharp
[Fact]
public void Faster_Matrix_Multiply_Range()
{
    Gen.Int[10, 100]
    .Select(n => (n, a: new double[n, n], b: new double[n, n], c: new double[n, n]))
    .Faster(
        t => MulIKJ(t.n, t.a, t.b, t.c),
        t => MulIJK(t.n, t.a, t.b, t.c));
}
```

Tests are in xUnit but could equally be used in any testing framework.

More to see in the [Tests](Tests).

### Configuration

Sample and Faster accept configuration parameters. Global defaults can also be set via environment variables.

```powershell
$env:CsCheck_SampleSeed = '657257e6655b2ffd50'; $env:CsCheck_SampleSize = 1000; dotnet test -c Release --filter SByte_Range; Remove-Item Env:CsCheck*

$env:CsCheck_FasterSigma = 200; dotnet test -c Release --logger:"console;verbosity=detailed" --filter Faster; ; Remove-Item Env:CsCheck*
```