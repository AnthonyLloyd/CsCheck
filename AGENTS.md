# AGENTS.md

Guidance for AI coding agents working in, or generating code that uses, **CsCheck**.

## What CsCheck is

CsCheck is a C# **property-based / random testing** library (QuickCheck-style).
Generation *and* shrinking are both driven by [PCG](https://www.pcg-random.org),
so shrinking is automatic, parallelized, and reproducible from a seed.

Key consequence for code generation: **there are no `Arb` classes and you never
write shrinkers.** You compose a `Gen<T>` and call a `Sample`/`Faster`/`Hash`
method on it. Shrinking is handled for you.

It supports: random, model-based, metamorphic, parallel/concurrency, causal
profiling, regression, and performance testing.

CsCheck is **test-framework agnostic**. It has no dependency on any test
runner: a failure is signalled by a thrown exception (or a `false` return from
`Sample`), so it works with xUnit, NUnit, MSTest, TUnit, or no framework at all
(e.g. a plain console app). It signals failure by throwing — it does not call
any framework's assertion API.

> Note: everything below about TUnit and the Microsoft.Testing.Platform runner
> describes **how this repo's own tests are set up**, not a requirement for
> using CsCheck. When generating tests for another project, use whatever test
> framework that project already uses.

## Build, test, and run (this repo)

The repo targets the .NET SDK pinned in `global.json` and uses the
**Microsoft.Testing.Platform** runner (not `dotnet test`'s legacy VSTest path).
This repo's tests happen to be written in **TUnit**, but that is a choice of
this repo, not a CsCheck requirement.

```powershell
# Build the library
dotnet build CsCheck/CsCheck.csproj -c Release

# Run the full test suite (this is the CI command)
dotnet run -c Release --project Tests --output Detailed --disable-logo --no-progress

# Run a subset by test tree filter
dotnet run -c Release --project Tests --no-restore --disable-logo --output Detailed --treenode-filter /*/*/GenTests/*
```

The `Tests/` project *is* the example collection — when you need an idiomatic
usage pattern, find a similar test there first and follow it.

## Conventions

- **Framework**: CsCheck is framework-agnostic — it signals failure by throwing
  an exception (or `Sample` returning `false`), so it works with any test
  framework or none. The conventions below are specific to *this repo's* test
  suite, which uses TUnit.
- **Repo tests**: marked `[Test]` (TUnit) and live in `Tests/`.
- **Output**: in this repo, pass `TUnitX.WriteLine` to any `writeLine:`
  parameter (see `Tests/TUnitX.cs`). In another project, pass that framework's
  output sink (e.g. xUnit's `ITestOutputHelper.WriteLine`), or `Console.WriteLine`
  if you have no framework.
- **Style**: `Nullable` and `ImplicitUsings` are enabled; `LangVersion` is
  `preview`. Warnings are errors (`TreatWarningsAsErrors`, `WarningLevel 9999`,
  `AnalysisMode All` with Meziantou.Analyzer). Keep generated code warning-clean.
- **No reflection** is used in the library; prefer composing generators over
  reflection-based helpers.
- A failing `Sample` shrinks to the simplest example and prints a **seed**
  string. Re-run with `seed:` to reproduce exactly.

## Writing tests — the core entry points

Compose a `Gen<T>` with `Gen.*` factories and LINQ (`Select`, `SelectMany`,
`Where`, query syntax), then terminate with one of:

### Random testing — `Sample`
Return `false` or throw to signal failure.

```csharp
[Test]
public void Long_Range()
{
    (from t in Gen.Select(Gen.Long, Gen.Long)
     let start = Math.Min(t.V0, t.V1)
     let finish = Math.Max(t.V0, t.V1)
     from value in Gen.Long[start, finish]
     select (value, start, finish))
    .Sample(i => i.start <= i.value && i.value <= i.finish);
}
```

### Model-based testing — `SampleModelBased`
Generate `(actual, model)`, apply random `Operation`s to both, assert equal.

```csharp
Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
.SampleModelBased(
    Gen.Int.Operation<SetSlim<int>, HashSet<int>>(
        (ss, i) => ss.Add(i),
        (hs, i) => hs.Add(i)));
```

### Metamorphic testing — `SampleMetamorphic`
Do the same thing two different ways from one initial sample; assert equal.

```csharp
Gen.Dictionary(Gen.Int, Gen.Byte)
.Select(d => new MapSlim<int, byte>(d))
.SampleMetamorphic(
    Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
        (d, t) => { d[t.V0] = t.V1; d[t.V2] = t.V3; },
        (d, t) => { if (t.V0 == t.V2) d[t.V2] = t.V3; else { d[t.V2] = t.V3; d[t.V0] = t.V1; } }));
```

### Parallel / concurrency testing — `SampleParallel`
Run operations sequentially then in parallel; passes if at least one
linearization matches. No `repeat` needed (unlike QuickCheck).

```csharp
Gen.Const(() => new ConcurrentQueue<int>())
.SampleParallel(
    Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
    Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _)));
```

### Performance testing — `Faster`
Statistically asserts the first function is faster than the second and (by
default) produces equal output.

```csharp
Gen.Byte.Array[100, 1000]
.Faster(
    data => data.Aggregate(0.0, (t, b) => t + b),
    data => data.Select(i => (double)i).Sum(),
    writeLine: TUnitX.WriteLine);
```

### Regression testing — `Single` + `Check.Hash`
`Single` pins a generated example by seed; `Check.Hash` checks a hash of
results, caching them so later runs report the first difference.

```csharp
Check.Hash(h =>
{
    h.Add(portfolio.Positions.Select(p => p.Profit));
    h.Add(portfolio.Profit(fxRate));
}, 5857230471108592669, decimalPlaces: 2);
```

## Common generator building blocks

- Primitives: `Gen.Int`, `Gen.Long`, `Gen.Double`, `Gen.Byte`, `Gen.Bool`,
  `Gen.Char`, `Gen.String`, `Gen.Guid`, `Gen.DateTime`, ...
- Ranges via indexer: `Gen.Int[0, 9]`, `Gen.Long[start, finish]`.
- Sub-ranges: `Gen.Single.Unit`, `Gen.Double.Unit`, `Gen.Char.AlphaNumeric`,
  `Gen.Int.Uniform`.
- Collections: `gen.Array`, `gen.Array[n]`, `gen.Array[min, max]`, `gen.List`,
  `gen.Array2D`, `Gen.Dictionary(keyGen, valGen)`.
- Combinators: `Gen.Select(...)` (tuples expose `.V0`, `.V1`, ...),
  `Gen.SelectMany`, `gen.Where(...)`, `Gen.OneOf(...)`, `Gen.Const(() => ...)`,
  `gen.Null()`, `Gen.Recursive<T>((depth, self) => ...)`.

## Configuration parameters (optional args on Check methods)

`iter` (default 100), `time` (seconds), `seed` (reproduce a case),
`threads` (default = logical CPUs), `timeout` (Faster, default 60s),
`print`, `equal`, `sigma` (Faster, default 6), `replay` (SampleParallel,
default 100).

Global defaults via environment variables: `CsCheck_Iter`, `CsCheck_Time`,
`CsCheck_Seed`, `CsCheck_Sigma`, `CsCheck_Threads`.

## Gotchas for agents

- Do **not** write `Arbitrary`/shrinker code — it doesn't exist here and isn't
  needed.
- `Classify` and `Faster` make `writeLine:` effectively required to see output.
- The `Dbg` module is a temporary debug aid; its API may change between minor
  versions — don't rely on it in committed library code.
- Prefer `Gen.Const(() => new ...())` (factory) over a shared instance for
  parallel/model tests so each run gets a fresh state.
