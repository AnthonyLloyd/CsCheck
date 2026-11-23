# Spec: a document's requirements, proved, and code that conforms

Use `Spec` for a stateful thing a document specifies: a protocol, exchange rules, a regulation, a lease or locking scheme. Write a small pure transition system over an immutable `record` state, plus named requirements, each carrying the sentence it comes from. Then check it from the one definition: `Exhaustive` first, then `Faults`, then the conformance checks. CsCheck's `docs/Spec.md` on GitHub is the full guide, `docs/SpecDesign.md` explains the design, and `Tests/Specs` has worked examples; start from `Tests/Specs/SpecIntroTests.cs`.

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

spec.Exhaustive(writeLine);         // a proof when the space closes, and the shortest path to any violation; a space that doesn't close passes, with a note
spec.Sample(writeLine);             // random walks with shrinking, for models too big to close
spec.Faults(writeLine);             // mutation testing for the requirements, over the .Fault(...)s declared; with none it only reruns Exhaustive
spec.SampleFaults(writeLine);       // the same, walked rather than proved, when the space won't close
spec.Mermaid();                     // the reachable state graph as a Mermaid flowchart, for a small model
spec.Conform(() => new Engine(), Apply, writeLine);           // does the real code conform, on sampled traces
spec.ConformExhaustive(() => new Engine(), Apply, writeLine); // on every transition, down shortest paths
```

## Building the model

- Build the spec in a method each test calls: a spec freezes the first time it runs, and adding to it afterwards throws. Duplicate ids or names, an `on:` or `per:` naming an action that doesn't exist, and a boundary that excludes the initial state are all rejected before anything runs. `Invariant` is checked on the initial state too.
- The state must be an immutable `record` or `record struct`, for value equality, and every counter must saturate or be bounded, or `Exhaustive` never closes. Never put a `Dictionary`, `List` or array in it: record equality compares those by reference, so two states built with different collection instances are never equal, and the space usually never closes.
- Keep a `record struct` state narrow in bytes as well as bounded in values: every guard and requirement takes it by value, so padding costs about a nanosecond per byte per transition (1.84x from 12 to 96 bytes). Pack flags into an int rather than a field each.
- Abstract argument values to the relations the document's rules are written in, such as `TooLow`, `Expected` and `TooHigh`, not raw values. Pick the smallest domain that could make each requirement false: a domain of one value can make a requirement pass without testing anything.
- Make the illegal state representable, such as a count rather than a bool, so the bad state can be reached and caught.
- Model steps no coarser than the code's. Split an operation into separate start and complete actions so every interleaving is covered. One model took a decision and its compare-and-swap as one step, and let every lease lapse at the same instant, so it couldn't see a race. Splitting late commits, half-step leases and a lingering old process out of those steps caught four mutants that had survived every other test.
- Saturate first. Bound, rather than saturate, a value whose order matters, disable the action at the bound, and declare the states where a bound runs out `Terminal`. When saturation shows through, write it into the requirement: `a.Queued == Math.Min(b.Queued + 1, 2)`. Only when a counter really can't saturate, add `.Boundary(s => ...)`, which closes the search over that region instead of giving up at `maxStates`. The step out of the boundary is still checked. Don't add a boundary to a model that already closes.
- Put the last observation, such as the message received or the messages sent, in the state. That makes every requirement a pure predicate over `(before, after)`.
- You can write the state's `Equals` and `GetHashCode` yourself, to drop data nothing reads or merge interchangeable subjects. If it's too coarse, the proof is silently false, and the requirements must treat the merged subjects alike. If interchangeable subjects show different coverage rows, the equality is broken.
- Dependencies run implementation, then specification, then tests. The system under test owns the vocabulary (message kinds, enums, configuration constants) and knows nothing about the spec; the spec does `using static` on the implementation's types. Never make the implementation reference its spec: it could then not be shipped without it. Keep the implementation, its spec and its tests in three files, each named after the one type it holds.
- `weight:`, at least 1, only steers the random walks: `Sample`, `Conform`, `SampleFaults` and `GenTrace`. Give crash, restart, clock and trace-ending actions low weights and protocol steps high ones: in one port that made deep steps 30 to 50 times more frequent.
- `.Print(s => ...)` sets how a state prints. When the search gives up at `maxStates`, a note names the widest state fields, and says if no state was revisited. It parses the printed state, so it needs a printer or `ToString` that keeps the record shape `Type { A = 1, B = 2 }`.
- `spec.GenTrace(minSteps, maxSteps)` gives a `Gen<Trace<S>>` to use in your own tests.

## Requirements

- The forms are `Invariant`, `Reachable`, `Rule(then)`, `Rule(when:, then:)`, `Rule(on:, then:)`, `Rule(on:, when:, then:)`, `Never`, `Never(on:)`, `AtMost`, `Response`, `Precedes` and `NeverAfter`, each with an id and the sentence it comes from.
- For a claim about every step, use `Rule(then)`, never a `when:` of `true`: the first reports "every step", while the second prints a step count that looks like how often the interesting case happened and is only how many steps ran.
- Pair each safety requirement with one saying the legitimate case still works, such as a live holder is never refused. Otherwise a system that refuses everything passes.
- `NeverAfter(until:)` scopes an obligation between two events, reopening on the next `after`. Prefer a state field when the state can say whether the scope is open: it costs the same, prints in the counterexample, and other requirements can read it. `until` is checked before `never`, so an `until` that holds whenever `never` does means the requirement can never fail: `Faults` lists it as unexercised.
- `Response(within:, per:)` bounds a consequence in domain time, and `per:` names the action that advances the deadline. `per:` only constrains paths where that action keeps happening, and can't name an argument. `cancel:` closes the obligation without a response, such as when the connection drops. A `Response` that's really about the environment's latency can't be proved.
- A requirement without `over:` is one instance with one deadline or count, so one key's event can discharge another key's obligation. Pass the keys as `over:`, and `AtMost`, `Response`, `Precedes` and `NeverAfter` track each key separately, with the key passed to their predicates.
- `Response` is for consequences that take time: a response holding on the trigger step itself doesn't discharge it, so a consequence in the same step, such as answering a TestRequest with a Heartbeat, is a `Rule`. `Precedes` is the opposite: both predicates holding on one step satisfies it. `Precedes` covers the whole trace, so a rule per connection also needs a `Never` written over the state.
- At most 16 `Response` and `AtMost` requirements in all, and 64 `Precedes` and `NeverAfter`; each element of `over` takes one slot. `within` and `times` are at most 254.
- For an action that must make progress, add `Never(id, quote, on: "Name", (b, a) => a.Equals(b))`.

## Reading the report

- `Triggered` `NEVER` means the requirement's when, on or trigger never happened, so its passing row proves nothing.
- `Fired` is per action and argument case, so `NEVER` there means that case is dead when `Exhaustive` closed, and only that it was never sampled for `Sample` and `Conform`.
- `Self-loops` counts steps that left the state unchanged.
- A non-zero deadlock count prints a path to one, each step naming what else was enabled where it was taken, since every action is disabled at the dead end itself. `Sample` and `Conform` count deadlocked walks, in `DeadlockTraces`, and leave `DeadlockStates` at 0.
- `Unresolved` counts `Response` obligations still open where a run ended: at the end of each walk for `Sample` and `Conform`, and at each Terminal or deadlocked state for `Exhaustive`. If walks were cut short by `maxSteps`, use longer walks. If they ended in a Terminal or deadlocked state, the obligation can never be met there, so decide whether the model should allow that.

## Asserting

`Exhaustive` throws only on a violation, or on a `Reachable` that never held in a closed space where nothing was pruned. Not closing, stopping at `maxDepth`, deadlocks, `Unresolved` obligations and `NEVER` rows all pass; `Unresolved` is only in the printed table, so assert the rest, here in NUnit:

```csharp
var report = spec.Exhaustive(writeLine);
Assert.That(report.Closed);
Assert.That(report.DeadlockStates, Is.EqualTo(0));
Assert.That(report.NeverTriggered, Is.Empty);
Assert.That(report.NeverFired, Is.Empty);
Assert.That(report.States, Is.EqualTo(1_284));
Assert.That(report.Transitions, Is.EqualTo(9_640));
```

- Pin the size of the space, not only that it closed: every other assertion has the form "no counterexample was found", which a search that explored too little also satisfies.
- `ConformExhaustive` also returns normally when the space doesn't close, checking as far as the search got, so assert `Closed` there too. For `Sample` and `Conform`, assert `DeadlockTraces` is 0.
- To show a requirement can fail, `spec.Exhaustive(out var violation)` returns the shortest violation instead of throwing. `throwOnViolation: false` only stops the throw, and doesn't return the violation.

## Faults

`Faults` is mutation testing for the requirements: you declare faults, small wrong versions of the system, and it checks a requirement catches each one.

- Declare faults with `.Fault(name, (b, a) => when, (b, a) => a with { ... })`, or the `on: "Action"` forms. The spec is first checked with no fault, and must pass.
- `Faults` and `SampleFaults` return a `SpecFaultsReport`. Assert which requirement caught each fault, not just that something did: `spec.Faults(writeLine).CaughtBy("no heartbeat when idle")` should be `"HB-KEEPALIVE"`. A fault caught by the wrong requirement passes while leaving the intended one unproven.
- `Faults` gives each fault an `Outcome` of `Caught`, `NotDetected` or `Inconclusive`, the last for a fault whose search didn't close, which never throws. It throws `No requirement detects these faults: ...` unless `throwOnUncaught: false`. So assert the report's `Inconclusive` is empty, and with `throwOnUncaught: false` its `Uncaught` too. `Steps` is the length of the shortest counterexample.
- `SampleFaults` gives `Caught` or `NotFound`, and throws `No requirement detected these faults in the walks sampled: ...` on `NotFound` unless `throwOnUncaught: false`. It spends `iter` on each fault, plus one pass without any, and a `Reachable` can't be the requirement that catches a fault there.
- Distrust a fault's exclusions: in one worked example they hid a missing requirement.

## Conformance

- `apply(implementation, transition)` performs the step on the implementation, then compares it with the spec's state after the step. The `Transition<S>` gives the step as `Action` (its name), `ArgIndex` (an index into the array the action's arguments came from) and `Arg`, and the spec's states as `Before` and `After`. It returns `bool`, or `string?`: null conforms, any other string diverges, even an empty one, and non-empty text prints as the `Reason:`.
  ```csharp
  static string? Apply(Engine engine, Transition<State> step)
  {
      switch (step.Action)
      {
          case "Recv": engine.Receive(Inbound[step.ArgIndex]); break;
          case "Tick": engine.Tick(); break;
      }
      return engine.Status == step.After.Status && engine.Expect == step.After.Expect ? null
          : $"engine {engine.Status} {engine.Expect}, spec {step.After.Status} {step.After.Expect}";
  }
  ```
- Compare everything the abstraction keeps, not a few fields, and the decision each subject would make next as well as the state.
- Both checks stop at the first, shortest divergence, so a known defect hides every divergence behind it.
- Prefer `ConformExhaustive` when the space closes. It drives the implementation along every transition the proof counts, each from a fresh instance down a shortest path, so it needs no weights or shrinking, and its counts (`ConformedStates`, `ConformedTransitions`) equal the proof's.
- It covers one path per state, so keep `Conform`'s random walks for behaviour that depends on how a state was reached. In one model, an id reused once a counter reached 5 was missed by `ConformExhaustive`, whose shortest paths never get that far, and found by random walks within 100. Even 100,000 walks took under a second.
- Each transition costs as many implementation steps as the depth of the state it leaves, plus one. Pass `clone:` when the implementation can be copied: each transition is then one step on a copy, which made one model's check about ten times faster on one thread. A copy must share nothing a step changes. Pass it by name, since a lambda such as `s => s.Clone()` also converts to `writeLine`'s `Action<string>`. A divergence on a copy is replayed on a fresh instance before it's reported, and if that conforms, the first line reads `..., on a clone but not when replayed on a fresh instance: the clone is not a faithful copy, or the implementation is not deterministic`.
- The implementation must be deterministic.
- Make the function that maps the implementation to the spec's state allocation-free: it runs on every step.
- A big model's checks can take tens of seconds, and longer on a slower CI machine, so check your CI's per-test timeout.
- Mutation-test the implementation against the conformance tests, as the `finding-test-gaps` skill does. Mutants that survive every check point at what the model abstracts away.

## Size and threads

- `maxStates` defaults to 10,000,000, which takes about 2 to 3 GB.
- `Exhaustive` and `Faults` default to one thread. Use more when the delegates are expensive or the model is cyclic, with many revisits; with cheap delegates on a model that only advances, more threads ran at about 0.8x the speed.
- `Spec.Sample`, `SampleFaults`, `Conform` and `ConformExhaustive` default to every CPU. On more than one thread, the spec's delegates, `create`, `apply` and `clone`, and the state's `Equals` and `GetHashCode` must be thread-safe.
- `Spec.Sample` defaults to walks of 1 to 24 steps (`minSteps`, `maxSteps`), 100 of them; for a model that won't close, raise `maxSteps`, and `iter` or `time`. It takes `seed:`, and `Boundary` doesn't apply to it.
- `Mermaid` defaults to `maxStates: 200`. Mermaid's renderer refuses diagrams over 50,000 characters by default, so lower `maxStates` if the diagram shows an error. It doesn't check requirements.
