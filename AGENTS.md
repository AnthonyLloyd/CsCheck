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
(e.g. a plain console app). It signals failure by throwing; it does not call
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

The `Tests/` project *is* the example collection: when you need an idiomatic
usage pattern, find a similar test there first and follow it.

## Conventions

- **Framework**: CsCheck is framework-agnostic; it signals failure by throwing
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
- A failing `Sample` shrinks to the simplest example and prints a line with a
  **seed** plus `(N shrinks, M skipped, K total)`. Re-run with `seed:` to
  reproduce exactly. `skipped` counts shrink-phase candidates that weren't
  smaller than the current minimal failure, so they were never asserted; it is
  **not** errors or `Where`-filtered inputs, and is `0` in a passing run.

## Writing tests: the core entry points

Compose a `Gen<T>` with `Gen.*` factories and LINQ (`Select`, `SelectMany`,
`Where`, query syntax), then terminate with one of:

### Random testing: `Sample`
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

### Model-based testing: `SampleModelBased`
Generate `(actual, model)`, apply random `Operation`s to both, assert equal.

```csharp
Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
.SampleModelBased(
    Gen.Int.Operation<SetSlim<int>, HashSet<int>>(
        (ss, i) => ss.Add(i),
        (hs, i) => hs.Add(i)));
```

With `writeLine:` set a table of how often each operation ran is written, rows named `Op0`, `Op1` by argument position.
Add `classify:` over the model state to split each operation by the state it acted on, which is how to check the
interesting cases were reached and not just the easy one. Both are inert without `writeLine:`.

### Metamorphic testing: `SampleMetamorphic`
Do the same thing two different ways from one initial sample; assert equal.

```csharp
Gen.Dictionary(Gen.Int, Gen.Byte)
.Select(d => new MapSlim<int, byte>(d))
.SampleMetamorphic(
    Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
        (d, t) => { d[t.V0] = t.V1; d[t.V2] = t.V3; },
        (d, t) => { if (t.V0 == t.V2) d[t.V2] = t.V3; else { d[t.V2] = t.V3; d[t.V0] = t.V1; } }));
```

### Specification testing: `Spec` + `Exhaustive` / `Sample` / `Faults` / `Conform`
For a stateful thing specified by a document (a protocol, exchange rules, a regulation). Write a small pure
transition system over an immutable `record` state plus named requirements each carrying the sentence it comes
from, then check it four ways from the one definition. See `docs/Spec.md` for how, `docs/SpecDesign.md` for why, and `Tests/Specs/FixEngineSpec.cs`.

```csharp
var spec = Spec.From(State.Connected)
    .Action("Recv", Inbound, (s, _) => s.Status != Disconnected, (s, m) => s.Inbound(m), weight: 30)
    .Action("Tick", s => s.Status != Disconnected, s => s.Tick(), weight: 20)
    .Terminal(s => s.Status == Disconnected)
    .Invariant("EXPECT-POSITIVE", "MsgSeqNum: value must be positive", s => s.Expect >= 1)
    .Rule("TESTREQ-ANSWERED", "Respond to a TestRequest with a Heartbeat echoing the TestReqID.",
        when: (b, a) => b.Up && a.Got(In.TestRequest, Seq.Expected), then: (b, a) => a.Put(Out.Heartbeat))
    .Never("EXPECT-MONOTONIC", "SequenceReset may only increase the expected sequence number.",
        (b, a) => a.Expect < b.Expect)
    .Response("LOGOUT-COMPLETES", "Terminate anyway if the confirming Logout does not arrive.",
        trigger: (b, a) => a.Status == LogoutSent && b.Status != LogoutSent,
        response: (b, a) => a.Status == Disconnected, within: Interval + 1, per: "Tick");

spec.Exhaustive(TUnitX.WriteLine);   // proof when the state space closes; shortest path when it does not
spec.Sample(TUnitX.WriteLine);       // random walks with shrinking, for models too big to close
spec.Faults(TUnitX.WriteLine);       // mutation testing for the requirements themselves
spec.SampleFaults(TUnitX.WriteLine); // the same table walked rather than proved, when the space will not close
spec.Mermaid();                      // reachable state graph as a Mermaid flowchart, for a model small enough to look at
spec.Conform(() => new Engine(), Apply, TUnitX.WriteLine); // does the real code conform to the spec
```

Rules for generating this:
- The state **must** be an immutable `record`/`record struct` (value equality) and every counter **must** saturate,
  or `Exhaustive` never closes. Abstract argument values to the relations the document's rules are written in
  (`TooLow`/`Expected`/`TooHigh`), not raw values.
- Dependency direction is **implementation → specification → tests**. The system under test owns the vocabulary
  (message kinds, enums, configuration constants) and knows nothing about the spec; the spec does
  `using static Tests.TheImplementation;`. Never make the implementation reference its specification; it could then
  not be shipped without it. In this repo the trio lives in `Tests/Specs/` as `Thing.cs`, `ThingSpec.cs` and
  `ThingTests.cs`, so each file is named exactly after the single type it holds.
- Saturate first; only when a counter genuinely cannot saturate add `.Boundary(s => ...)`, which closes the search
  over that region instead of giving up at `maxStates`. The step out of the boundary is still checked, but an unheld
  `Reachable` becomes a note rather than a failure because unreachability needs full closure. Do not add a boundary
  to a model that already closes.
- Put the last observation (message received, messages emitted) **in the state**. That is what makes every
  requirement a pure predicate over `(before, after)`.
- Requirement forms: `Invariant`, `Reachable`, `Rule(then)`, `Rule(when/then)`, `Rule(on:/when:/then:)`, `Never`, `AtMost`, `Response`, `Precedes`, `NeverAfter`. For a claim about every step use `Rule(then)`, never a `when:` of `true`: the first reports `every step`, the second a count that looks like vacuity information and is not.
- `NeverAfter(until:)` scopes the obligation between two events, reopening on the next `after`. Prefer a state field
  when the state can say whether the scope is open: it costs the same search state, prints in the counterexample, and
  other requirements can read it. Six of the seven worked examples use a field; the one that uses `until:` had the
  requirement come back **unexercised** from `Faults`, because its `until` closes the scope on the same condition that
  would let `never` fire. A field cannot close itself out of the way.
  `Response(within:, per:)` bounds in domain time; `per:` names the action that advances the deadline.
- `Response` is for consequences that take time: a response holding on the trigger step itself does **not** discharge
  the obligation. A property whose consequence happens in the triggering step (answer a TestRequest with a Heartbeat)
  is a `Rule`. `Precedes` is the opposite: its two predicates holding on one step satisfies it.
- Read `Triggered` in the report: `NEVER` means the requirement passed vacuously. `Fired` is per (action, argument) case, so `NEVER` there means that case is dead. A non-zero `deadlock` count prints a path to the first one.
- Assert the size of the space (`report.States`, `report.Transitions`), not only that it closed. Every other assertion
  has the form "no counterexample was found", which a search that explored too little also satisfies.
- Assert which requirement caught each fault, not just that something did:
  `Assert.That(report.CaughtBy("no heartbeat when idle")).IsEqualTo("HB-KEEPALIVE")`. A fault caught by the wrong
  requirement passes while leaving the intended one unproven.

### Parallel / concurrency testing: `SampleParallel`
Run operations sequentially then in parallel; passes if at least one
linearization matches. No `repeat` needed (unlike QuickCheck).

```csharp
Gen.Const(() => new ConcurrentQueue<int>())
.SampleParallel(
    Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
    Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _)));
```

### Performance testing: `Faster`
Statistically asserts the first function is faster than the second and (by
default) produces equal output.

```csharp
Gen.Byte.Array[100, 1000]
.Faster(
    data => data.Aggregate(0.0, (t, b) => t + b),
    data => data.Select(i => (double)i).Sum(),
    writeLine: TUnitX.WriteLine);
```

### Regression testing: `Single` + `Check.Hash`
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

- Do **not** write `Arbitrary`/shrinker code; it doesn't exist here and isn't
  needed.
- `Classify` and `Faster` make `writeLine:` effectively required to see output.
- The `Dbg` module is a temporary debug aid; its API may change between minor
  versions; don't rely on it in committed library code.
- Prefer `Gen.Const(() => new ...())` (factory) over a shared instance for
  parallel/model tests so each run gets a fresh state.
- **`skipped` in a failure message is not a problem.** It counts shrink-phase
  candidates whose generated `Size` was not smaller than the current smallest
  failure, so they were skipped rather than asserted. It does **not** mean
  inputs threw, were `Where`-filtered, or that coverage was lost. It only
  becomes non-zero *after* a failure triggers shrinking; a passing test always
  reports `0 skipped`. Do not treat a large `skipped` count as a secondary bug.
