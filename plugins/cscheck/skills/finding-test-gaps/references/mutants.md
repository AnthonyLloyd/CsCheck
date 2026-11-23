# Mutants in detail

## Kinds of mutant

The kinds that found gaps worth a test in the trials behind this skill, most productive first:

1. Force a condition. Set an `if`, `?:`, `when` or loop condition, or a bool that's returned or assigned, to `true` or `false`, or drop one operand of `&&` or `||`. Examples are one item from a chain of `x == A || x == B` checks, one clause of a compound rule, or the guard that picks a fast path.
2. Drop an item from a list, set or switch: a name from a set of names to skip, a `case`, an `else if`.
3. Delete a statement or assignment that adds to the result: an `Add`, a `Sort`, one member of a `with`, a `Where` or other filter. When one survives, ask which input would expose it. It's often a kind of input, with fields of its own, that no test or generator has.
4. Swap a call for its sibling: `Any` and `All`, `Min` and `Max`, `First` and `Last`, `Single` and `SingleOrDefault`, `Floor` and `Ceiling`, `AddDays` and `AddMonths`, a priority queue's `EnqueueDequeue` and `DequeueEnqueue`. Think beyond these: any call with a near twin of the same signature.
5. Swap two arguments of the same type, such as `start` and `end`, `from` and `to`, or the two sides of a subtraction or a `CompareTo`.
6. Change a constant, a flag or a default: a time of day, a tolerance, an enum flag, or a `bool` parameter negated where it's read. Change it where the library reads it: C# compiles a public or internal `const`, an enum member's value and a default parameter value into each caller, so the tests and other libraries keep the old value and the mutant survives falsely. The run warns about these, though not for a private one, whose uses are all in the recompiled library.
7. Change what an early return gives, such as the value for an empty input.

Boundaries were the noisiest kind: of 7 moved from `<` to `<=`, only the one on a count was worth a test, and none on an instant was. So move a boundary only where the two sides can really be equal, such as a count or a whole date, whatever today's callers pass; closing-test-gaps' edge probes test the boundaries themselves.

`candidates.cs` lists kinds 1 to 5 from the code, with boundaries, `true` and `false` flipped, early returns of a number, dropped null checks and `??` fallbacks, local time for UTC, a dropped `.Date`, `FirstOrDefault` made `First` and `x / 2.0` made `x / 2`. It also drops each flag of an enum's `A | B`, and doesn't pass on a value the code was given: an optional or nullable parameter, or one of its type's primary constructor or a field, such as a serialiser it was built with, but not a logger or a cancellation token. In trial 2 these showed a dropped TTL, serialiser and comparer.

With `--rest`, it also lists Stryker.NET's other mutations, under each member after "The rest of Stryker.NET's":
- operators swapped: arithmetic, comparisons that aren't already forced or dropped as an operand, bitwise, compound assignments, `++` and `--`, a unary minus, and `&&` and `||` in a chain of three or more;
- `!`, `~` and `checked` dropped, and `is` made `is not`;
- strings emptied, an empty one given text, `IsNullOrEmpty` and `IsNullOrWhiteSpace` letting null, an empty or a blank string through, and `Substring` and `Trim` made empty;
- `??` always taking its fallback, on a value the code wasn't given or a logger or cancellation token, or trying its fallback first, an empty collection given an item, and one of one item, or of more than 10, emptied;
- `break`, `continue`, `yield` and `goto` skipped, and a member's body emptied;
- Stryker's LINQ, string and `Math` swaps, and its regex mutations, in a pattern passed to `Regex` or in a `[GeneratedRegex]`.

They found nothing new in the trials, so ask for them only where they're the member's own logic: arithmetic in a calculation, a string in code that parses, formats or matches text, a regex's pattern, bit operations in flags or a hash, or a jump in a loop's own logic, and keep those that change it. A negated condition or bool, and `&&` swapped for `||` in a chain of two, aren't listed, since forcing the condition both ways and dropping each operand already show what they would.

It leaves out logging, tracing, metrics, sleeps, cancellation checks, assertions, disposal, exception messages, and anything outside a member's body except a `[GeneratedRegex]`. It reads only the syntax, so some candidates won't compile, such as a swap of two arguments of different types, or of the sides of a `DateTime` minus a `TimeSpan`, `-` for a `+` that joins two string variables, `??` swapped onto a value that can't be null, a named argument whose parameter has no default, a sibling the type doesn't have, or a boundary that leaves a pattern unable to match or repeats a later arm, such as `is <= 0` made `is < 0` on a `Length`.

A list can't see what a value means, so write these by hand for each member, where the code invites them:
- a similar but wrong value: the neighbouring enum member (`MidpointRounding.ToEven` for `MidpointRounding.AwayFromZero`), a sibling property (`Capacity` for `Count`), field, calendar or time zone, or the last item for the first;
- another source for a date or time: the local date where the UTC one is meant, a stored date where today's is meant, or a day added;
- an argument not passed on that the list doesn't see: a value the member works out, or one passed under another name, given as `null` or its default, or a call that transforms it dropped, such as `Path.GetFullPath(path)` for `path`;
- two arguments of the same type swapped where one is a call, such as `existing` and `Load(key)` in `Merge(existing, Load(key))`, which the list leaves out since most such pairs differ in type;
- a call that makes values comparable dropped, such as the `Math.Abs` in a tolerance;
- one too few or too many in a length, count or index, such as `maxLength - 1`, or an index argument given as `0`;
- a constant, flag or default changed where the library reads it, as kind 6 says;
- a defensive copy dropped, so the result shares an input's array;
- a sequence made lazy, where it's enumerated twice or after its source changes;
- a `Dispose` whose job is disposing what it holds, such as a list's items, which the list leaves out with the rest of disposal;
- integer division from a dropped cast, or the wrong rounding.

The repo's own fix history is the best source of more: the bugs its past fixes show are the likeliest ones still in it, so turn the commonest into kinds of mutant.

These were only noise in the trials, so don't write them:
- logging, tracing, metrics, sleeps, cancellation checks, assertions, disposal (except the `Dispose` above) and exception messages, which `candidates.cs` leaves out too, and parallelism defaults;
- date arithmetic moved by one, such as `AddDays(n)` to `AddDays(n + 1)`;
- a dropped normalising call such as `Trim`, or a case-sensitive comparison, except in code that parses or matches text;
- separators that only collide for contrived input;
- a value that never decides the result;
- input outside what the code must support, such as a null the serialiser never writes.

Judge whether a mutant changes behaviour from the code, not its comments, which can be stale. A mutant that only makes the code slower, such as a removed pruning `return`, is equivalent unless the docs promise a complexity; then, if it's worth a test, count calls through a key type whose `CompareTo` increments a counter, rather than timing it.

## The spec

```json
{
  "testProject": "tests/MyLib.Tests",
  "filter": "FullyQualifiedName~DateRangeTests",
  "exclude": [],
  "mutants": [
    {
      "id": "end-included",
      "bug": "Contains(End) is true, so a range includes the end it excludes",
      "file": "src/MyLib/DateRange.cs",
      "find": "date >= Start && date < End",
      "replace": "date >= Start && date <= End"
    }
  ]
}
```

- Every mutant needs an `id`, a `bug`, a `file`, a `find` and a `replace`. The `id` is letters, digits, `.`, `-` and `_`, starting and ending with a letter, digit or `_`, and unique ignoring case, since it names folders. The `bug` is the effect a caller would see, which a survivor prints. `find` must occur exactly once in the file, and a `\n` in it matches the file's line ending there, CRLF or LF. An empty `replace` deletes the `find`. If `find` doesn't occur exactly once, the run stops before building, naming the lines when it occurs more than once, and so it does for an unknown field or a `replace` equal to its `find`. The JSON can have comments and trailing commas.
- `candidates`, optional, names a file `candidates.cs` wrote, relative to the spec. A mutant there is given by its `id` alone, such as `{ "id": "Fact.cond1" }`, with a `bug` of its own if its listed one won't do, and takes its file, find and replace from the file. Write any other mutant out in full beside them. Run `candidates.cs` again after the code changes, since its finds are taken from the file as it was.
- `expect`, optional, names the test a mutant is aimed at, by method name, as `exclude` does, or several separated by commas, such as a test written to kill it or a property meant to replace examples. A survivor then says which test let it through, and a kill by other tests says so. Don't name one for every mutant of a sweep: in trial 2 the test named failed on 285 of the 288 mutants killed, so the names told nothing that reading the survivors doesn't.
- Keep each mutant inside a method body: the test project isn't rebuilt, so a changed signature fails when the tests run, not at compile time. The run warns about one that isn't, one in code an `#if` leaves out of the build, and one that only changes whitespace or comments.
- `filter` is passed as `dotnet test`'s `--filter`, which VSTest takes, as do MSTest and NUnit on Microsoft.Testing.Platform, or for TUnit as a `--treenode-filter` such as `/*/*/UtilsTests/*`. A filter that starts with `--` is passed to the test app as its own arguments, such as `--filter-class MyLib.Tests.CalcTests` for xUnit v3 on Microsoft.Testing.Platform, which has no `--filter`. Filter to the file's own test classes, since a whole suite can take several times as long. The filtered tests must pass without a mutant.
- `rerunFilter`, optional, filters the rerun of survivors described below, such as `Category!=Integration` to keep slow or external tests out of it. Without it the rerun runs the whole test project.
- `exclude` lists tests that fail on purpose, such as a property showing a bug, by method name: their failures don't count, in the filter or in the rerun. That matches NUnit, TUnit, xUnit and MSTest names, with or without data arguments, but not a test given a display name of its own.
- For a test project with several target frameworks, the run takes the first unless `framework` names one. A library with several is mutated in the build the test project uses.
- `testProject` and each `file` are relative to the repo root. Write paths with `/`, which works on every system.

## A run

- Keep the JSON outside every project folder, since a run that's killed leaves copies of the mutated files beside it, which a project's `**/*.cs` would compile.
- It never edits a source file, so neither the IDE nor `git status` shows a live mutant. Snapshot tests such as Verify's are the exception: a mutant that breaks one writes a `.received.` file next to the test, so delete those after the run.
- It builds the test project in Release, as the coverage step does. Each mutant then compiles, in a fraction of a second for a small library and a few seconds for a large one, from a copy of its file in the run's folder, so `[CallerFilePath]` and path-scoped `.editorconfig` settings see that path rather than the file's. Four run at once (`--parallel`, 1 to 16), in copies of the test output next to it in `bin`. A run that's killed leaves those copies there until the next run of that test project and framework deletes them.
- Tests that clash when run side by side, such as on a fixed port, a shared cache file or timing, fail the baseline in the copies; if they pass alone, the run drops to one copy, for the filter and for the rerun. A test that only fails under load and doesn't test the mutated code can go in `exclude`, to keep the copies. Four copies of a suite with heavy tests can run the machine out of memory, so use `--parallel 2` for one like CsCheck's own.
- When the spec has a `filter`, it reruns the survivors against the whole test project, or `rerunFilter`'s tests, since a member only another test class tests looks covered, but its mutants survive the filter. A survivor whose status the rerun changes, to KILLED, CRASHED, TIMEOUT or ERROR, is marked "outside the filter", and a rerun whose tests come to none is skipped with a warning. The whole project includes performance tests, such as CsCheck's `Faster`, whose timing under several copies usually drops the rerun to one copy; a kill by a timing test can be false, so rerun it with `--parallel 1`. `--filter-only` skips the rerun, for a project whose whole suite is too slow.
- Each mutant comes back KILLED with the tests that failed, SURVIVED with its `bug`, TIMEOUT, CRASHED, NO_COMPILE with the compiler error, or ERROR when the tests exited with an error but no test failed (see its log). CRASHED means the mutant brought down the test host, which usually counts as a kill. The project's warnings-as-errors don't apply to mutants, so a mutant that drops a null check still compiles; an error severity set in a `.globalconfig` does apply, and makes such a mutant NO_COMPILE.
- The timeout is `--timeout`, by default 5 times the slowest baseline run and at least 30 s. The rerun keeps its own default, which `--timeout` can only lengthen. A baseline gets at most 30 minutes. One that times out stops the run, except the rerun's, which only skips the rerun. A TIMEOUT doesn't settle a mutant: a loop it made endless is a real kill, but a busy machine gives false ones, so rerun those with a longer `--timeout` or `--parallel 1`.
- Logs, TRX files and `results.json`, with each mutant's id, status, detail, bug, expect, warning, failing tests and seconds, go to `<json name>-run/` next to the JSON. Each run without `--only` replaces that folder, and a second run of the same spec stops while one is going. A spec rejected before the build, for bad JSON, a `find` that doesn't occur once or a missing file, leaves the last results alone; a run stopped after that, because another run is using the test project's output or for a file the project doesn't compile, has replaced them. To rerun a few mutants, such as a group after writing its test, pass `--only <id,id>`: it keeps `results.json`, even when it fails, with the earlier results of the spec's other mutants and their logs, so the sweep's counts outlive the rerun. If a mutated project's source changes during the run, the run says so, since the results may not match the code then.
- It works with VSTest and Microsoft.Testing.Platform test projects, built by the repo's SDK, which must be 8 or later. It reads the results from TRX files, so a project on Microsoft.Testing.Platform needs the `Microsoft.Testing.Extensions.TrxReport` package, which TUnit and MSTest's `MSTest` package bring.
