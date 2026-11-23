---
name: writing-cscheck-tests
description: Writes CsCheck tests for .NET code that find bugs rather than just pass. Also use it when writing or reviewing unit tests for pure logic, to consider whether a property would find more or could replace them. E.g. "add a CsCheck property for this parser", "write a round trip test for X", "check Equals and GetHashCode agree", "is my new version faster", "model this protocol as a Spec and check the code conforms", "test this class for thread safety", "why does my property pass when the code is broken". Covers Sample, Equality, SampleModelBased, SampleMetamorphic, SampleParallel, Spec, Faster, causal profiling, Single and Check.Hash, and what changed in CsCheck 5. To hunt for the bugs themselves, use closing-test-gaps.
license: Apache-2.0
compatibility: Written for CsCheck 5, which targets net10.0 and net11.0. Probes need the .NET 10 SDK or later.
---

# Writing CsCheck tests

CsCheck is a random testing library for .NET. You compose a generator, `Gen<T>`, and end it with a check such as `Sample`. There are no `Arb` classes and you never write a shrinker: a failure shrinks to a simpler case by itself, reproducibly from a seed. CsCheck works with any test framework or none, since a check fails by throwing `CsCheckException`; in `Sample`, the function you pass can return false instead.

## Choose the check

| To check | Use | Read |
|---|---|---|
| A rule over many inputs: a round trip, an oracle, an invariant | `gen.Sample(...)` | this file |
| `Equals`, `GetHashCode` and `IEquatable<T>` agree, and each field counts or doesn't on purpose | `gen.Equality(...)` | this file |
| A stateful type against a simple model of it, such as a `HashSet` | `SampleModelBased` | [stateful.md](references/stateful.md) |
| Two ways of reaching one result agree | `SampleMetamorphic` | [stateful.md](references/stateful.md) |
| A type is safe to use from several threads | `SampleParallel` | [stateful.md](references/stateful.md) |
| A protocol or rule book, proved over every reachable state, and code that conforms to it | `Spec` with `Exhaustive`, `Faults`, `Conform`, `ConformExhaustive` | [spec.md](references/spec.md) |
| One implementation is faster than another | `Faster` | [performance.md](references/performance.md) |
| Which part of a concurrent program to speed up | `Causal.Profile` | [performance.md](references/performance.md) |
| Results don't change from version to version | `Single` and `Check.Hash` | [regression.md](references/regression.md) |

Read the file for the check before writing one: each has rules the API doesn't make obvious.

## Before writing

- Read the code under test first: most bugs turn up in the reading, and a property is easier to aim at code you've read. To hunt for bugs, follow closing-test-gaps, which confirms its candidates with the properties described here.
- Find the project's existing CsCheck tests, by searching for `using CsCheck`, and follow them, reading what their generators make. CsCheck's own `Tests` project on GitHub is its example collection.
- If the test project doesn't reference CsCheck and the request didn't ask for it, say which property you'd write and ask before adding the package. Add it with `dotnet add package CsCheck`. Under central package management the `PackageReference` has no version, which goes in `Directory.Packages.props`. A language server can report CS0246 for CsCheck's types until it reloads the project, so trust the build.
- Add `using CsCheck;` even where you'd write names in full: `Array`, `List`, `HashSet` and friends are extension properties, so `CsCheck.Gen.Int.Array` without the `using` is error CS1061.
- CsCheck 5 targets net10.0 and net11.0. A test project on net8.0 or net9.0 needs CsCheck 4.9.1, whose API differs as "Coming from CsCheck 4" lists.
- Pass the framework's output to `writeLine:` while writing a test: NUnit's `TestContext.Progress.WriteLine`, xUnit's `ITestOutputHelper.WriteLine`, TUnit's `s => TestContext.Current?.OutputWriter.WriteLine(s)` (a lambda, since `?.` can't make a method group), MSTest's `TestContext.WriteLine`, or `Console.WriteLine`. With it a pass writes `Passed N iterations.` Remove it afterwards, except where it carries the result: the classify overload, `classify:` tables, a passing `Faster` and `raiseexception: false`.
- For async code, `SampleAsync` takes a `Func<T, Task<bool>>` or `Func<T, Task>`, with `threads` as the number of concurrent async workers. `SampleModelBasedAsync` and `FasterAsync` are the async forms of theirs.

## Generators

Compose them with LINQ: `Select`, `SelectMany`, `Where` and query syntax.

- **Primitives:** `Gen.Int`, `Gen.Long`, `Gen.Short`, `Gen.Byte`, `Gen.UInt`, `Gen.ULong`, `Gen.Int128`, `Gen.Double`, `Gen.Float`, `Gen.Decimal`, `Gen.Bool`, `Gen.Char`, `Gen.String`, `Gen.Guid`, `Gen.DateTime`, `Gen.DateOnly`, `Gen.TimeOnly`, `Gen.DateTimeOffset`, `Gen.TimeSpan`, `Gen.Enum<T>()`.
- **Ranges** with the indexer, ends included: `Gen.Int[0, 9]`, `Gen.Long[start, finish]`, `Gen.DateTime[start, finish]`, `Gen.Char['a', 'z']`, and `Gen.Char["abc"]` or `Gen.String["abc"]` to draw from the characters given. **Sub-ranges:** `Gen.Int.Positive`, `Gen.Int.NonNegative`, `Gen.Int.Uniform`, `Gen.Double.Unit`, `Gen.Float.Unit`, `Gen.Char.AlphaNumeric`, `Gen.DateTime.Utc`, `Gen.DateTime.Local`.
- **Collections:** `gen.Array`, `gen.List`, `gen.HashSet`, `gen.ArrayUnique`, `Gen.Dictionary(keyGen, valueGen)` and `Gen.SortedDictionary(...)`. Their length is uniform from 0 to 127, or 1 to 128 with `.Nonempty`; set it with `[n]`, `[min, max]` or `[Gen<int>]`. The length shrinks first. `gen.Array2D` draws each dimension from 0 to 127, and takes `[a, b]` for a fixed a by b, or `[Gen<int>, Gen<int>]`. `Gen.Shuffle(array)` permutes the array it's given, and `Gen.Shuffle(array, length)` or `(array, min, max)` picks that many of its elements.
- `HashSet`, `ArrayUnique`, `Dictionary` and `SortedDictionary` need that many distinct elements or keys: after 1,000 duplicates in a row the test fails with "The generator threw." So `Gen.Int[0, 9].HashSet` or `Gen.Enum<T>().HashSet` almost always fails; give a small domain a small length, such as `Gen.Int[0, 9].HashSet[0, 5]`.
- **Combinators:** `Gen.Select(g1, g2, ...)` gives a tuple, or a selector's result; `Gen.SelectMany`; `gen.Where(...)`; `Gen.OneOf(...)` of values or generators; `Gen.Frequency((weight, value or generator), ...)`; `gen.Nullable()` for a value type and `gen.Null()` for a class, both null 20% of the time unless given another fraction, such as `gen.Null(0.1)`; `Gen.Recursive<T>((depth, self) => ...)`.
- `Gen.Select` takes up to 16 generators with a selector, or 8 without one. For more, nest them, `Gen.Select(Gen.Select(a, b, c, d), Gen.Select(e, f, g, h), (x, y) => ...)`, which shrinks the same as one flat `Select`. Query syntax (`from a in ga from b in gb select ...`) has no limit but shrinks the earlier values first, so keep it for values that depend on earlier ones.
- `Gen.Const(x)` hands out the same instance every time; `Gen.Const(() => new X())` makes a new one per sample.
- `OneOf` and `Frequency` shrink toward their earlier arguments, so put the simplest case first, such as the leaf before the node in a `Recursive`. `Gen.Recursive` doesn't limit depth: return the leaf generator once the depth it passes reaches a maximum.

What the defaults don't give you:
- `Gen.Short`, `Gen.Int`, `Gen.UInt`, `Gen.Long`, `Gen.ULong`, `Gen.Int128` and `Gen.UInt128` favour small values, give their extremes on about 1 draw in 64 (`MaxValue` for the unsigned ones, where 0 is common anyway), and shrink toward 0. `Gen.Byte`, `Gen.SByte` and `Gen.UShort` are uniform, and so is a range such as `Gen.Int[a, b]`, with no extra weight on its ends.
- `Gen.Double` covers -1e50 to 1e50 (`Gen.Float` and `Gen.Decimal` 1e25), mixing integers, simple fractions, numbers of up to four significant digits at every scale, and uniform values, but never NaN or infinity. `Gen.Double.Special` and `Gen.Float.Special` give NaN, the infinities, the extremes, `Epsilon` and small integers 1 time in 16, and raw bit patterns otherwise.
- `Gen.Char` and `Gen.String` only make printable ASCII, `' '` to `'~'`. For other text use `Gen.Char[char.MinValue, char.MaxValue]`, lone surrogates included, or `Gen.String[Gen.Char[...], min, max]`.
- `Gen.DateTime` is uniform over every tick from year 1 to 9999, with `Kind` unspecified, so nearly every date is centuries from today. `Gen.DateTimeOffset` covers 1800 to 2200, with any whole-minute offset up to 14 hours.

What makes a generator find bugs:
- **Give usable inputs.** Build them so every sample tests something, such as valid dates in the range the code handles rather than any `DateTime`; then the code rejecting one is a failure. Where filtering is simpler than building, use `Where`: it draws again, and after `CsCheck_WhereLimit` (100) rejections in a row the test fails with "The generator threw." and the inner exception "Failing Where max count", so a generator that rarely gives usable inputs fails loudly.
- **Never skip inside the check**, such as an early `return true` for inputs the code rejects: a skipped sample counts as a pass. One property skipped nearly all of its random dates like that, and the skip hid a bug in the fifth week of a month. Don't check less for some inputs either: a property that checked only the median below 5 items, with `if (a.Length < 5) return <a weaker check>`, hid a bug in the quartiles at 1 and 2 items. Where the rule really differs for some inputs, state it for them, or give them a property of their own.
- **Generate the edges on purpose,** since a uniform range almost never hits them: `Gen.Frequency((1, Gen.OneOf(a, b)), (9, Gen.Int[a, b]))` gives the ends a tenth of the time. An offset of exactly 0 was 1 value in 36,001 of one generator, and a bug at 0 survived until it was `Gen.OneOf(Gen.Const(0), Gen.Int[0, 36_000])`. Making half of the generated ranges 1-10 days long found two bugs that longer ranges never hit.
- **Don't filter away a region.** A `Where`, or an implication, that drops a whole region hollows a property as a skip does: a `Where` that kept business hours dropped every weekend, which is where the bug was. Generate such regions on purpose: weekends, holidays, the hours either side of a cut-off, and time zones with and without daylight saving. Pass the code a `TimeZoneInfo` or a `TimeProvider` rather than relying on the machine's zone.
- **Generate what production passes, and the rest of the domain.** Find how the callers build the input and the values every caller passes, and make sure the generator gives those, since one built from the types alone can miss them. Don't stop there: a failure on input the code must support but no caller makes yet, such as a weekend date where today's callers only pass business days, is a bug too, only a less urgent one.
- **For an `IEnumerable<T>` parameter, generate lazy sequences too,** not only arrays: a `Count` fast path answers arrays and lists, so its bug only shows on a sequence that can be enumerated only once. Where the code returns lazy sequences of sequences, enumerate them in the order the real caller does, since some only work when each inner one is read before the next.
- **For a wire format such as JSON, build both sides' inputs through their real serialisers,** not hand-written JSON, fixtures or comments. In one planted case the fixtures lacked a field one side adds, which was the bug.
- **Generate every shape the format's spec or doc comment allows,** not only those in the sample data: each fraction length from 0 to 9 digits, and both `Z` and offsets. A timestamp parser threw for the 1-6 digits its doc comment allows, because every sample payload had 9, and an RFC 3339 parser rejected the 8-9 digits that Go and Java write.
- **Give a parser malformed input too:** valid input with junk or a stray delimiter after it should be rejected. Parser combinators such as Pidgin stop where the grammar does unless it ends by requiring the end of input, so a template with a stray `}` after it parsed silently, as if the `}` weren't there.
- **Look at what a generator gives** by running it once through the classify overload, `gen.Sample(x => x.Kind.ToString(), writeLine)`, whose string is a label, not a failure message: it writes how often each label came up, even when the test fails. A `/` in a label nests rows with subtotals, and a null label counts as `Null Count`. `logger: Logging.CreateTycheLogger()` writes the samples for Tyche, a viewer for property-based testing data, instead; dispose it when done.
- **Keep each sample cheap,** since a longer run is how you trust a pass. Moq's `Mock.Of<T>(x => x.P == value)` costs about 1 ms a call, dozens of times slower than `new Mock<T>()` with `Setup(x => x.P).Returns(value)`.

## Properties that find bugs

Write them against the public API. Three kinds find bugs:
- **A round trip**: format then parse, serialise then deserialise, an id to its display name and back.
- **An oracle**: a simple version to compare with, such as counting business days one at a time; a rule the docs state; or, for a port of another library, the original's definition. Compare a port with its original first: that found one bug before any test ran, and explained three more within a minute of their shrunk cases.
- **An invariant**: a schedule's dates increase from start to end, a sorted output stays sorted, or a value that should change smoothly, such as one interpolated between dates, has no jumps. The smoothness one found two bugs the round trips couldn't see.

Idempotency (normalising twice gives what normalising once does) and commutativity (independent updates in either order give the same result) are cheap to check too. Prefer a generator and `Sample` to a loop over hand-picked values: it covers more, and shrinks a failure to a simple case.

Then:
- A round trip, or a property that compares a method with itself, can't see a bug both sides share, such as a holiday rule both directions call, or a formula that's wrong the same way both times. That needs an oracle, an invariant or an example test.
- Compare canonical values, such as a date or a regenerated output, not strings, when inputs have old spellings.
- To test that a distribution is right, such as a shuffle being uniform, count outcomes into buckets and call `Check.ChiSquared(expected, actual)`, which needs every bucket to expect more than 5.
- A new property that fails has probably found a bug. Check the shrunk case against the code and its docs before changing the property, and never weaken the generator or the check to make it pass.

### Equality

Equality contracts break more often than they look: in one codebase an `Equals` and `GetHashCode` ignored a field, a `CompareTo` gave 0 for unequal values, and `hash ^ x?.GetHashCode() ?? 0` bound as `(hash ^ x?.GetHashCode()) ?? 0`, so a null reset the hash. `gen.Equality()` checks `Equals` both ways, `IEquatable<T>` and `GetHashCode` agree, on equal clones and on random pairs; `gen.Equality(comparer)` checks an `IEqualityComparer<T>`. Declare the fields to check that each one counts, or doesn't, on purpose:

```csharp
genOrder.Equality(f => f
    .Compared((o, v) => o with { Id = v }, Gen.Int)
    .Ignored((o, v) => o with { CachedTotal = v }, Gen.Decimal));
```

- A field that changes equality but isn't declared fails the check, with the inner exception "Equality or GetHashCode is affected by a field that is not declared as compared or ignored" and the two values, but not the field. A declared field is named by its setter's source text, or by `name:`, in the inner exception. An `Action<T, V>` setter works for a mutable type.
- Each field's generator must give two different values within 100 tries. For a field compared after normalising, such as rounded or case-insensitive, generate values that stay different after it, or pass a comparer.
- Declare only fields that always, or never, change equality. A field whose effect depends on others, such as equality on `Math.Max(A, B)`, fits neither, and leaving it out fails too, since the two instances still differ in it: hold such fields fixed in the generator, or check that type with plain `gen.Equality()`.
- `.Case<TArm>(...)` covers a hierarchy of subtypes, and C# unions on net11.
- `CompareTo` has no check of its own: write a `Sample` that `Sign(a.CompareTo(b)) == -Sign(b.CompareTo(a))` and that it's 0 exactly when the values are equal, and that it's 0 on `gen.Clone()` pairs and transitive on triples.

## Failing readably

- **Never assert inside `Sample`.** NUnit records every failing `Assert.That`, which buries the shrunk case under dozens of failures. An exception the code throws is fine: CsCheck catches it and shrinks it like a false, and keeps it as the `InnerException`. Since CsCheck 5 its text isn't copied into the message.
- A failure's message is a seed line with `(N shrinks, M skipped, K total)`, then the shrunk input, cut at 5,000 characters, with an array or list of more than 12 items shown as `L=n [first 6 ... last 6]` and other collections in full up to 999 items: pass `print:` to see more. `The generator threw.` means the generator failed, not the check. `skipped` counts shrink candidates that weren't smaller than the failure found so far, so they never ran: it isn't errors or `Where`-filtered inputs, and a large count is no problem.
- **Shrinking spends the same budget:** after the first failure, the remaining iterations or seconds only try smaller cases, so a failing run of the default 100 iterations barely shrinks. To shrink further, rerun with the printed seed as `seed:` and a larger `iter` or `time`; the seed's case runs first.
- **CsCheck reports only the smallest failure.** To find the other kinds, leave the kind just found out of the generator with a `Where` on its input shape, rerun, and repeat until the property passes: one property showed three bugs in three runs. Then take the `Where` out, so the test still fails on the first kind. Where the domain is small enough to list, such as every weekday for a few years, a probe that loops over all of it once shows every kind of failure with its count in one run.
- **Once a bug is fixed, keep its shrunk case as a plain example test.** A seed reproduces it only while the generator and CsCheck stay the same.

## Trusting a pass

One passing run isn't proof, since every run draws new inputs. Before trusting a new property:
- Run it longer in Release, filtered to the test: `dotnet test -c Release -e CsCheck_Time=60 --filter <name>`, or `time: 60` while you try it; a `SampleFaults` test then takes (faults + 1) times as long. Or repeat the test. One property passed several runs before it found its bug. A seed only reproduces a failure; it adds no coverage.
- Then remove the `time:` or `iter:` you added, as with `writeLine:`, so the test runs at the suite's default. If 100 iterations can miss a case, such as the one failing name among a few dozen, make each iteration check every name rather than raising `iter`.
- Break the code on purpose, with one small change the property should catch, and check it fails within a long run. If it passes, the property is hollow. finding-test-gaps' `mutants.cs` does this without editing the repo; its `--only` rerun of a sweep's survivors is this check.
- `Sample` runs on every logical CPU at once, so the check must be thread-safe: make what it changes inside it or in the generator, with `Gen.Const(() => new ...)`, or pass `threads: 1`.
- `iter: 0` or `time: 0` passed as arguments runs nothing and passes: only the settings are validated, not the arguments.

## Replacing example tests

A trusted property can make the example tests of the same code redundant. To find which:
- List the mutants of the members the examples test with finding-test-gaps' `candidates.cs`, give each one the property's name as its `expect`, and run them with its `mutants.cs`. A mutant that other tests kill and the property doesn't is reported as `by <tests>, not <property> as expected`. Rerun those with `--only`, run longer as finding-test-gaps' step 3 says, since a property can miss a mutant in one run and kill it in the next. The tests that still kill one the property misses check something it doesn't.
- Any other example is redundant if coverage doesn't drop without it and the property checks everything it asserts, which only reading shows in full.
- Even then, keep a fixed bug's shrunk case; an example whose expected value comes from outside, such as the docs or another system's output, since the property's oracle can share the code's mistakes; and an example of an edge case the generator only reaches by chance, until it gives it on purpose.
- Say which examples are redundant, and delete them only when asked.

## Settings

Optional parameters on the checks: `iter` (default 100), `time` (seconds), `seed`, `threads` (the logical CPUs, but 1 for `Exhaustive` and `Faults`; below 1 means the default) and `print`. `Faster`, `SampleModelBased`, `SampleMetamorphic`, `SampleParallel` and `Spec` add their own, such as `equal`.

Environment variables set the defaults: `CsCheck_Iter`, `CsCheck_Time`, `CsCheck_Seed`, `CsCheck_Threads`, `CsCheck_Timeout`, `CsCheck_Sigma`, `CsCheck_Replay`, `CsCheck_AllocAll`, `CsCheck_WarmUp`, `CsCheck_Ulps` (default 4: `Check.Print` shows a double within that many ulps as a shorter number), `CsCheck_WhereLimit` (100) and `CsCheck_SingleLimit` (1,000,000).
- An explicit `time:` wins, then `CsCheck_Time`, then an explicit `iter:`, then `CsCheck_Iter`, so `CsCheck_Time` lengthens every test that doesn't pass `time:`. `CsCheck_Seed` applies to every test, so pair it with a test filter.
- They're read once, when `Check` is first used; set `Check.Iter`, `Check.Time` and the others in code to change them during a run. A bad value fails every CsCheck call with a `TypeInitializationException`. Booleans accept `true`/`false`, `yes`/`no`, `y`/`n` and `1`/`0`.

## Coming from CsCheck 4

Code written for 4.x, including most CsCheck code a model has seen, needs these changes:
- `Gen.OneOfConst` and `Gen.FrequencyConst` are now overloads of `Gen.OneOf` and `Gen.Frequency`.
- `Gen.UInt4` to `Gen.UInt2048` are gone: `Gen.UInt[0, N - 1]` gives the same values. `Gen.UInt128` now generates `UInt128`, so a cast such as `(int)i` still compiles but means something else.
- `Check.ModelEqual` is gone: models compare with `Check.Equal`, so bags count duplicates.
- `Dbg`, `IRegression`, `RegressionExtensions` and `HashHelper` are gone, and `Hash.Add` takes collections. `ILogger` is `ISampleLogger`, and `Logging.CreateLogger` and `Logging.LogProcessor` are gone: use `Logging.CreateTycheLogger()`.
- `MedianEstimator`, `Classifier`, `Timer` and `ThrowHelper` are internal, as are the `Hash` and `MedianEstimate` constructors.
- `GenOperationAsync<T>` is gone: `SampleParallel` operations must be synchronous.
- `Exhaustive`, `Sample` and `Conform` return `SpecReport<S>` instead of `SpecReport`, as does the new `ConformExhaustive`. `Exhaustive` takes `writeLine` first, and `AtMost<T>` takes `times` after `over`. `SampleFaults` reports `FaultOutcome.NotFound`, not `NotDetected`, for a fault it misses.
- Seeds and `Check.Hash` expected values change for `Gen.Decimal`, `Gen.Double` and `Gen.Float`, and for the integer generators that now give their extremes on 1 draw in 64.
- Invalid settings throw, where they used to be clamped or ignored.
