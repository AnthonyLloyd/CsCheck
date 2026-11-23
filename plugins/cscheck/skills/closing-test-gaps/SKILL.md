---
name: closing-test-gaps
description: Hunts for bugs in C# and .NET code by reading it against what should agree with it (its siblings, docs, callers, history and the code it was ported from), probing the edges of what it must support, and confirming each candidate with a small probe or a CsCheck property, ranked by how likely each is to be real and to be live. Use when asked to find bugs or to check that code is right, e.g. "find the bugs in this parser", "is this calendar code right", "what could go wrong in this class", "check this for edge cases". To measure what the tests miss, use finding-test-gaps; to write the tests, writing-cscheck-tests.
license: Apache-2.0
compatibility: Probes need the .NET 10 SDK or later.
---

# Hunting for bugs

Read the code before testing it: in the trials, reading found most of the bugs before any test ran. Then show each candidate with a probe or a property, written as writing-cscheck-tests says. For parsers, formatters, converters and calendar rules, write the properties early.

1. **Settle what the code must support.** Its types, doc comments and validation, its documentation, and the values its real callers pass set its domain. Hunt over all of it, not only what the callers pass today: in the trials, 33 of 37 bug candidates had at most a 5% chance of being hit by today's callers. An input outside it isn't a bug unless the contract promises to handle or reject it, such as NaN or infinity where nothing can produce one, a negative count, or a date before the data starts. Note such an input as low priority at most. A general-purpose library's public API supports more than a helper with two callers. For code that translates another system's values, such as its codes or names, ask that system, read-only, and only with the user's agreement where it's production: the values it really sends, from its logs, fixtures or exports, are the domain, and its own list of what's valid, such as its registry of names, is the oracle for every output. In one run, logged inputs decided every verdict.
2. **Compare the code with what should agree with it:**
   - its siblings: the other overloads, the array and list versions, the path for each format or enum member. One array overload shuffled the caller's array in place where its list sibling copied it first;
   - its doc comments and documentation: a formula, a range, a promise of order;
   - any copy of it, such as a test helper that reimplements it, or the code it was ported from;
   - its history: `git log -L <first>,<last>:<file>` on a member's lines, or `git log -p -n 20 -- <file>`, shows special cases added or removed for one member, which its siblings may need too, workarounds that may have outlived their reason, and the code it replaced. In two runs the history led to three of six bugs;
   - its arithmetic: an operation done in a narrower type than its result, such as a `uint` shifted before it's widened, which drops the top bit, and values in different units compared or added, such as one fit's error measured on a log scale against others measured in ticks.
3. **Probe the edges of that domain,** with a probe or as generator cases:
   - the smallest sizes it accepts: empty, one and two items, where statistics and estimators often read state they haven't filled yet;
   - all values equal, all zero, already sorted;
   - each boundary the code or its docs name, from both sides;
   - a sign change where the domain spans zero, including `-0.0` in floating-point code that can produce it;
   - an integer type's extremes and a value with the top bit set, where the code does bit arithmetic or uses unsigned types;
   - what the callers produce at their own edges, such as zero from a timer too coarse to measure a fast action, or the first and last day of a range.
4. **Confirm a candidate** with a probe or a property. A probe is a `probe.cs` in a scratch folder that starts with `#:project <csproj>`, run with `dotnet run -c Release --file probe.cs`. Use Release, since an IDE or language server usually holds the Debug output open. A probe only reaches public types. Give it the tests' target framework, such as `#:property TargetFramework=net10.0`, since a file-based app otherwise takes the SDK's, and pass it its inputs at run time, such as from `args`, rather than as constants, since the compiler works constant expressions out itself and can get a different answer from the runtime the tests use: in one run, a probe written with constants gave the opposite answer to the tests. To check that a property would catch a bug like the one suspected, break the code on purpose with finding-test-gaps' `mutants.cs`, which compiles the change in a copy and leaves the repo alone.
5. **Rank the candidates before fixing anything,** by how likely each is to be real and to be live. For live, find every caller and read the arguments it passes, at the call rather than the whole calling method: in one run a bug needed two flags both true, and no caller passed them that way, so it couldn't happen. Where config drives the code, read the configs too, and where logs or metrics can show the effect, check them, read-only and with the user's agreement for production: in one run the logs took a bug from likely to certain. Leave a failing property failing until the bug is fixed, but don't commit it failing: mark it with the test framework's explicit or skip attribute, naming the bug, or leave it out of the commit.
6. **After a fix, test every sibling** of the fixed code, such as the other types, formats or cases it serves: 5 of 31 bugs in one study turned up while fixing two others.

## Report

Report the candidates ranked, each with its evidence (the probe's output, or the property's shrunk case), how likely it is to be real and to be live, and the fix it needs, before fixing any.
