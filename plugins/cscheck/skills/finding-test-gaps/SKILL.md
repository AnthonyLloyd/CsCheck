---
name: finding-test-gaps
description: Measures what C# unit tests don't check, with any test framework, CsCheck or not: code coverage for code no test runs, and a sweep of hand-picked mutants, compiled and run without editing the repo, for code the tests run but don't check. Use when asked how good the tests are or what they miss, e.g. "how good are the tests for LruCache", "what's untested in this project", "mutation test this method", "what do the tests on my branch miss", "do these tests actually check anything". It takes a test project, a source file, types or members, or a branch to compare with. To hunt for the bugs themselves, use closing-test-gaps, and to write the tests, writing-cscheck-tests.
license: Apache-2.0
compatibility: Needs the .NET 10 SDK or later, since the tools are C# file-based apps, NuGet access on their first run, and git to compare with a branch and for the use counts and callers. For SDK-style C# test projects on VSTest or Microsoft.Testing.Platform.
---

# Measuring what the tests don't check

Two measures, each for a different kind of gap:

1. **Coverage**, one run of the test project with coverage on: code no test runs.
2. **Mutants**, minutes per file: code the tests run but don't check. A mutant is a small bug written into the code, such as a dropped condition or swapped arguments. A mutant no test fails on is a bug the tests would miss.

They show how good the tests are, where new tests should go, and whether a new test checks anything. They don't find bugs by themselves: bugs come from reading the code and from properties, as closing-test-gaps describes, though the sweep's reading turns up candidates too. Both suit pure logic with fast, deterministic unit tests, not functional or regression tests, or tests that need a database, the network or real timers.

The tools are three C# file-based apps in this skill's `scripts` folder, written `<skill>/scripts/` below. Run each with `dotnet run --file`, as below: without `--file`, a project file in the current folder is built or run instead. The first run of each compiles it in a few seconds, and `mutants.cs` and `candidates.cs` restore Roslyn from NuGet then. `dotnet` takes its SDK from the `global.json` above the folder it runs in, so in a repo whose `global.json` pins SDK 8 or 9, run the tools from another folder: `mutants.cs` and `candidates.cs` then take the repo as `--root <repo>`, and `mutants.cs` builds it with the repo's own SDK. Keep reports, plans and logs in a scratch folder outside the repo, written `<scratch>` below, and send long output to a log file there rather than reading it all.

## 0. Scope and cost

- Settle what was asked. A question such as "how good are the tests" wants a report: measure, report the gaps, and offer the tests they need with what those would take. Write tests only when the request asks for them, or the user agrees.
- Settle the scope: files, types or members, or the members a branch changed (`--since`, step 1). The inputs in scope are everything the code must support, by its types, docs and validation, which for general-purpose code is more than its callers pass today.
- Coverage costs one run of the test project, so always start with it, in the background where your agent allows, and read the code and its tests while it runs.
- A sweep takes about 2 to 3 s of wall time a mutant, with four running at once and the survivors' rerun included: 3 to 7 minutes a file in the trials, and 25 to 45 for a session with its tests. Count the candidates with `candidates.cs` (step 2): its line that starts `Wrote` says how many it lists. For more than a few hundred, ask before sweeping: say what it will cost, and offer to start with the members with the most logic and callers.

## 1. Coverage

```
dotnet test -c Release <test project> --collect "Code Coverage;Format=cobertura" --results-directory <scratch>/coverage-1
dotnet run --file <skill>/scripts/uncovered.cs -- <scratch>/coverage-1 [<file or type names, or partial paths>] [--since <ref>]
```

For a test project with several target frameworks, add `-f <framework>` to the first command to run one. A Microsoft.Testing.Platform project, such as one using TUnit, collects the same report from its test app: `dotnet run -c Release --project <test project> -f <framework> -- --coverage --coverage-output-format cobertura --results-directory <scratch>/coverage-1`, where `-f` is needed only for several target frameworks. Collect each run into a new folder (`coverage-2` and so on), since `uncovered.cs` merges every report under the folder. Given file names, it prints each file's lines not run, its branches partly taken and its members; given none, it ranks the files; given `--since <ref>`, it shows the changed lines no test ran, in the named files if any, by file name rather than type. Read [coverage.md](references/coverage.md) for what the output means and the blind spots.

- Code no test runs needs a test before a mutant can say anything about it, and so do lines no test runs inside a member that does run, such as a branch for a holiday. Say what the code does and whether a caller depends on it, and test what matters. Make the first test an oracle or a property, not one example that runs every line: in one planted case, a test that ran every line of the method still passed with the bug, which only an input of three or more consecutive dates showed.
- Here, too, write the properties that reading the code shows are worth having, such as a round trip, an oracle or an invariant, in code that runs as well as in code that doesn't, as writing-cscheck-tests says. The sweep then measures them along with the old tests, so its survivors are what they all miss. When only measuring, offer them in the report instead.
- Once those tests are written, collect coverage again into a new folder, so the sweep knows which lines still don't run.
- Give each block of lines that still doesn't run one of these reasons. That today's callers don't pass an input that reaches it isn't one (step 0).
  - No input the code must support reaches it. Say whether it's a guard for a state that can't happen, which can stay; dead code, to offer to delete; or a bug, where a wrong condition or caller keeps out the input that should reach it, which goes with the bug candidates.
  - No test can reach it without changing the production code, such as a method that takes a concrete class that connects to a cache on first use. Name the change that would let a test reach it, such as an interface or a delegate in the class's place.
  - It isn't worth a test: it only logs, traces or records metrics, or nothing a caller sees depends on it.
  - It needs a test, but the request was only to measure. Name the test.
- A branch partly taken is a cheap lead: it has an outcome no test has seen.
- If coverage was the question, stop here and report.
- Otherwise, start the sweep's table from the latest coverage, with `uncovered.cs`, the files' names and `--sweep <scratch>/sweep.md`. That file gets a row per member, and the output lists each member's callers, which step 2 works from, so keep it in a log in `<scratch>`. `--sweep` needs file names and doesn't take `--since`, so after `--since`, name the changed files. Put the reasons above in the table's "Not run, and why" column.

## 2. Mutants

Read [mutants.md](references/mutants.md) before writing any: the kinds that found gaps worth a test, the kinds that were only noise, the spec and how a run behaves.

### Sweep every member

Writing mutants wherever a recipe seems to fit skips whole members, even ones with about 50 call sites. So sweep, don't sample:

1. Take the members from the table step 1 started, in source order: `uncovered.cs` listed each with the uses of its name elsewhere in the repo, its lines not run, and up to four of its uses outside tests and in them, with the arguments each passes. Uses go by name, so a private member shows none and a common name, such as `IsMatch`, can show only other types': find its callers with find-references. When starting from `--since`, the scope is the changed members it printed, and read the changed lines it lists that aren't code, since a changed const or signature can change members it doesn't name.
2. Sweep every member a caller reaches, directly or through the file's other members, and in a library, such as a shared project or a NuGet package, every public member, whether its callers are in the repo or not yet anywhere (step 0): in trial 2 the public members nothing called yet held 17 survivors worth a test. Take code with puzzled comments early (`WHY?`, `???`, a TODO), since nobody has pinned down what it does. Skip the rest as dead code, offer to delete it, and say so, but check a 0 with find-references first, since reflection, serialisers and DI call members by name, and a collection's lookups call `Equals` and `GetHashCode` without naming them.
3. Read the member's tests first, to see what they pin down and what they don't: mutants written with the tests in view are far more often real faults the tests miss. Compare the tests' arguments with the production callers', starting from the uses `uncovered.cs` listed: a value every caller passes and no test does, such as a null id, the production tolerance or a flag all callers set, is a common gap. But the callers are the least the tests should cover, not the most (step 0). Then list the candidates, which saves writing each mutant's text by hand:

   ```
   dotnet run --file <skill>/scripts/candidates.cs -- <file> --members <Type.member,member> --coverage <scratch>/coverage-N --out <scratch>/<name>.candidates.json
   ```

   Name the members as `uncovered.cs` printed them, and give it the latest coverage folder. The file it writes holds every candidate in the file whichever members are named, so the ids stay the same from one run to the next. It prints each member's candidates, with an id each, and counts those on lines no test runs, which it leaves out, since they can only survive. Keep every one a caller could see, even where you expect a test to kill it, and skip only what's equivalent, the don't-write list, and boundaries whose two sides can't really be equal, such as on an exact instant. Keep those that may not compile too: `mutants.cs` reports one that doesn't in about a second, which is quicker than sorting them out by hand. For the members whose own logic suits the rest of Stryker.NET's mutations, as mutants.md says, run it again with `--rest` and `--members` naming just those.

   Then write by hand, for each member, mutants.md's by-hand kinds and those the repo's bug history shows: in the blind trial they were nearly a third of the survivors. Note them in the sweep table's "By hand" column, or why a member has none.
4. Mutants only change code that's there, so they can't show a missing case: a key no map has, or an enum member one path handles and a parallel path doesn't. Note these as you read, and give them a test over every enum member or key, or one comparing the parallel paths.
5. While reading, note what a property could check, such as a round trip, an oracle or an invariant, and anything that looks like a bug. Hunt the bug candidates in step 3, where the survivors can point at more.
6. Keep the plan in the table step 1 started in `<scratch>/sweep.md`: fill in each member's mutants from the list and by hand, its property (or "none"), and what was skipped and why. Note the missing cases and bug candidates under the headings below it. That way nothing is skipped silently, and no property idea is lost before the tests are written. Write it out whole when you first fill it in, then change it with small edits rather than rewriting it, since in the trials output was the largest cost of a session.

### Run

List the mutants in a JSON spec in `<scratch>`, as mutants.md shows, written with your file-writing tool rather than through the shell, whose quoting mangles find text. Set its `candidates` to the file `candidates.cs` wrote, and give each candidate kept by its id alone. For a property from step 1, name it as the `expect` of the mutants it should kill. Run `mutants.cs` from the repo root, or with `--root`, as above:

```
dotnet run --file <skill>/scripts/mutants.cs -- <json>
```

It prints how long the mutants should take, a line as each one finishes, and then every result. Run it in the foreground, with a timeout of at least 3 s a mutant (step 0) plus the build, where your agent allows one, so the results come back in one call. Otherwise, or when there's reading left to do, such as the callers of the members swept, run it in the background with its output sent to `<json>.log`, and wait to be told it has finished rather than reading the log while it runs, since every look costs a turn. Don't edit the library or its tests until it finishes: the results would be for code that has since changed, and in one trial that cost a second sweep.

## 3. Act on the survivors

For each survivor, read the code and say what bug it stands for and why the tests miss it: name a test that runs its line and what its check misses, such as an equality check on a `DateTimeOffset`, which ignores the offset. It's a gap if the bug is plausible for an input the code must support. Then check the callers, which set its rank, not whether it counts: of the 37 bug candidates in the trials, 33 had at most a 5% chance of being hit by today's callers, and 20 of those were at least 80% likely to be real. Then do one of these:
- give it a test, as below;
- call it equivalent (it behaves the same for every input, e.g. `<` to `<=` in a clamp, where both branches give the same value at equality) and say why, with no test. A dropped defensive copy is equivalent only if neither the input nor the result can change afterwards; or
- leave it and say why, such as a tie-break in a private helper whose only caller can't produce a tie. First ask whether the caller is wrong: in one run, an exact-match branch no caller could reach turned out to be a bug in the callers' lookup key.

Survivors cluster around bugs: six on one path sat on two real bugs. So read a cluster of survivors as a bug candidate, and hunt it with those noted while reading, as closing-test-gaps describes, before writing tests.

Group the survivors that need a test by what would expose them: usually an input the tests lack, sometimes a check they make too loosely. One test that covers it usually kills the whole group. In one run, three survivors hid behind the only test date, 2023-01-01, which reads the same with day and month swapped, tests that only used a winter date, when their time zone matched UTC, and a check of a fraction to within a microsecond. A round trip over random dates and a property comparing exact ticks killed all three. A test of time zone handling should set its zone rather than use the machine's, since a mutant using local time is only killed where the zone is off UTC on the dates tested, which a UTC build server never is.

If the request was only to measure, stop here and report the groups with the test each needs. Otherwise write one test per group, preferring a CsCheck property written as writing-cscheck-tests says, and rerun the group's mutants with `--only` to show it kills them. Take its expected values from the docs, an oracle or a hand calculation, not from what the code returns now: a survivor shows the tests can't tell two versions apart, not which is right, and where only the code can say, the survivor is a bug candidate. Where the input is one known value, such as an expression from a production config, an example test is simpler.

- Coverage doesn't move for a property in code that already runs, so the `--only` rerun is its measure: the group's survivors are the mutants only it should kill, and one that still survives a long run shows the test is hollow.
- A mutant that only a CsCheck property kills can survive one run and die the next, since every run draws new inputs. Set `CsCheck_Iter` (such as 1000) in the environment of the run, which the tests inherit, to lengthen every check that doesn't pass its own `iter:` or `time:`. To settle one survivor, rerun it with `--only <id> --filter-only` and `CsCheck_Time` (such as 10), which overrides a check's `iter:` too. Without `--filter-only`, a mutant that survives again is rerun against the whole project, where every property takes that long.

## 4. Report

Report the few gaps that matter most, ranked, rather than every survivor, written once at the end, from sweep.md after a sweep:
- the scope, the lines run and why the rest don't, and the mutants killed, survived, equivalent and timed out, and those that didn't compile;
- for each gap: the member, its evidence (the lines not run, or the surviving mutants and the bug each stands for), the input or check that would expose it, and the test that now covers it, or the test it needs;
- separately, the bug candidates found on the way, ranked by how likely each is to be real and to be live, before fixing any.
