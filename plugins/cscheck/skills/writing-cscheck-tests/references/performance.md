# Performance tests: Faster and causal profiling

## Faster

`Faster` checks statistically that the first function is faster than the second. It times the two in pairs, counts which won each, and stops once one has won more often than a fair coin would by `sigma` standard deviations (default 6); ties don't count. The overloads that return values also check by default that both give equal output.

```csharp
Gen.Byte.Array[100, 1000]
.Faster(
    data => data.Aggregate(0.0, (t, b) => t + b),
    data => data.Select(i => (double)i).Sum(),
    threads: 1, writeLine: TestContext.Progress.WriteLine); // NUnit's output
```

The overloads:
- `Check.Faster(Action faster, Action slower, ...)`, and `Check.Faster(Func<T> faster, Func<T> slower, equal: ..., ...)`, for fixed work.
- `gen.Faster(Action<T>, Action<T>, ...)` and `gen.Faster(Func<T, R>, Func<T, R>, equal: ..., ...)` for generated inputs, with overloads for tuple generators up to 8 items, `(x, y) => ...`, and `FasterAsync` for async functions.
- For code so fast that a delegate call would show in the timing, struct forms of both take an `IInvoke`, or an `IInvoke<T, R>` with the type arguments written out: `gen.Faster<A, B, T, R>(new(), new())`.
- Parameters: `sigma` (6), `threads` (the logical CPUs), `repeat` (calls per timing, default 1), `timeout` (60 s), `raiseexception` (default true), `writeLine`, `allocAll` (count allocations on every thread, not just the timing one; `FasterAsync` always does, and has no `allocAll`), `warmUp`, and on the generator overloads `seed`. `equal: Check.EqualSkip` skips the output check.

### Reading the result

`writeLine` gets a line such as `25.12%[23.40%..26.90%] 1.34x[1.31x..1.37x] faster, sigma = 6.0 (36 vs 0), min = 1.204ms vs 1.612ms, alloc = 0B vs 48B, jit = ...`:
- the median time saved per pair, then the same as a speed ratio, each with its interquartile range in brackets, which is not a confidence interval;
- `faster` or `slower`, then the sigma reached, and in how many pairs each function won;
- the fastest single time of each, `alloc =` with one call's allocations of each, measured after a pass, and the JIT mode. `MAY BE TIER 0` there warns that it may have timed unoptimised code.

The line can start with `Time resolution too small try using repeat.` or `Inconsistent result try using repeat or increasing sigma.`

It fails in four ways:
- the line says `slower`, or says `faster` after `Inconsistent result ...` when the second function won more of the pairs;
- `Timeout!` and the line, when `sigma` isn't reached within `timeout` seconds, which is how two equally fast functions always end, so set a shorter `timeout` when they might tie;
- `Return values differ:` with both values, which isn't shrunk. From `gen.Faster` it includes `CsCheck_Seed=...`: pass that back as `seed:`, and its input runs first. `Check.Faster` and `FasterAsync` give no seed;
- either function throws, which the generator overloads report as `CsCheck_Seed=... T=<input>`, with the exception as the `InnerException`.

`raiseexception: false` turns only the first two into the line passed to `writeLine`, so keep `writeLine` there: without it, a slower or timed-out run passes silently. That line doesn't say `Timeout!`; its sigma is below the one asked for.

### The JIT decides most results

.NET first compiles a method quickly (tier 0), then recompiles it optimised after about 30 calls, in the background, and only once no new method has been called for 100 ms. A test run keeps calling new methods, so that can take the whole run: with tiering on, CsCheck's own suite never got its `Faster` functions past tier 0.

- One pair measured 7.5x apart at tier 0, 1.28x when optimised with profile-guided optimisation, and 11.3x with tiering off.
- On CI, or wherever `Faster` tests run inside a parallel suite, turn tiering off, as CsCheck's own Tests project does: `<TieredCompilation>false</TieredCompilation>` in the test project. That applies to every test in the project, so where the other tests shouldn't change, keep the `Faster` tests in a project of their own. With tiering on, warm-ups time out under that load.
- For numbers with dynamic PGO, keep tiering on and run the test on its own with `warmUp: true` (or `CsCheck_WarmUp=true`), which calls both functions until the JIT has finished with them, for up to 5 s, and with the tiering delay at 0: `<RuntimeHostConfigurationOption Include="System.Runtime.TieredCompilation.CallCountingDelayMs" Value="0" />` in an item group.
- Run in Release, compare results only within one JIT mode, and before calling a slowdown in one scenario a regression, rerun it with tiering off: the first scenario to touch a type's methods runs them at tier 0.

### Measuring well

- Pass `threads: 1`: the default runs pairs on every core, where they share caches and cores, so ratios differ.
- While measuring, pass `sigma: 20` for a steadier median, then remove it: it needs about 11 times the pairs, so close functions can time out.
- On Windows, `Stopwatch` ticks are 100 ns, so `Faster` can't resolve calls under about a microsecond there; Linux and macOS tick in nanoseconds. Give each timing a batch of work, such as arrays of 5,000 elements in all, each used once, or use `repeat`, which calls each function that many times on one input, so not on a function that changes it in place.
- Give each function its own loop or method, so they share no call site, and write one `Faster` per concrete type behind an interface. The JIT optimises a call site for the one type or delegate it saw most often, in roughly its last 100 calls, so a shared one favours whichever function ran last.
- Each function gets its own fresh copy of the input, with the same values. With near-identical code, the second function, which is always timed second, can win by up to 13% on data-dependent branches, which the branch predictor has just learned. To compare near-identical variants, give each its own values, such as by building them from `Random.Shared` inside the generator, with `equal: Check.EqualSkip`, and check the outputs agree in a separate `Sample`.
- Results can move 10-20% from run to run, so take the median of 3 runs before trusting a small difference. Separate processes are worse: timing two builds one after the other on a laptop couldn't settle a 10-20% question.
- To compare two versions of a change deep inside your own code, time them in one process with a temporary static switch, and remove it afterwards:
  ```csharp
  Check.Faster(() => { Lib.UseNew = true; Run(); }, () => { Lib.UseNew = false; Run(); },
      threads: 1, raiseexception: false, writeLine: TestContext.Progress.WriteLine);
  ```
  Both functions share `Run`'s call sites there, so keep it for changes behind the switch; where the two versions are separate methods, call each directly.
- On a CPU with performance and efficiency cores, keep timing runs on the performance cores: set the process's processor affinity on Windows, or use `taskset` on Linux. macOS has no affinity control.

## Causal profiling

Causal profiling answers which part of a concurrent program to speed up. It slows each marked region down, and virtually speeds it up by delaying the other marked regions instead, then measures how the total time moves. Code outside every region is never delayed, so mark all the work that runs alongside the region you're asking about.

```csharp
var region = Causal.RegionStart("parse");
// ... the work ...
Causal.RegionEnd(region);

Causal.Profile(() => Run(), iter: 10).Output(writeLine);
```

- Regions do nothing outside a profile, so they can stay in the code.
- Each iteration runs the action 1 + 6 times the number of regions. `iter` defaults to 100, but `time:` or `CsCheck_Time` replaces it, even an explicit `iter`, so keep the action short and leave `CsCheck_Time` unset when profiling. Its state is global, so never profile two things at once.
- In the table, `Time%` is the region's share of the total time across all cores. The `+10%` and `+5%` columns slow the region down, and `-5%` to `-20%` virtually speed it up. Each cell is the percentage by which the median run time fell, negative where it rose, with an error from the run times' interquartile ranges. A region whose speed-up columns stay near 0 isn't the bottleneck, however much time it takes.
