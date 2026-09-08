# Specification testing: design record

Why `Spec` is shaped the way it is, what the prior art does differently, and what the worked examples
changed while they were being written. The how-to is in [Spec.md](Spec.md); this is the reasoning behind it.

## Why this and not more model-based testing

CsCheck already has model-based testing (`SampleModelBased`) and parallel/linearisability testing
(`SampleParallel`), which puts it in the small group of libraries that have both. So "add model-based testing" is
not the gap. Reading across the prior art, the gap is that **every one of these tools checks a
transition at a time, and specifications are not written a transition at a time.**

- **`eqc_statem` / `eqc_component`** (Quviq QuickCheck, Erlang, closed source) is the origin of stateful PBT:
  symbolic command generation, per-command preconditions and postconditions, shrinking of command sequences.
  Everything since is a re-implementation of it. It checks postconditions per call.
- **PropEr** (`proper_statem`, `proper_fsm`) is the open-source Erlang equivalent. `proper_fsm` adds named states.
- **Hypothesis** `RuleBasedStateMachine` is the best ergonomics in the field: `@rule`, `@precondition`,
  `@invariant`, `initialize`, and `Bundle`s for flowing generated values between rules. `@invariant` runs after
  every step, which is one step beyond "assert at the end". Still no trace properties, no exhaustive mode.
- **ScalaCheck** `Commands`, **FsCheck** `Experimental.StateMachine`, **jqwik** `ActionChain`,
  **proptest-state-machine**, **quickcheck-state-machine**, **stateful-check**: all variations on the same
  per-transition shape. Stevan Andjelkovic's survey
  ([*The sad state of property-based testing libraries*](https://stevana.github.io/the_sad_state_of_property-based_testing_libraries.html))
  is the definitive map, and its own proposed next steps are "ship a short implementation" and "make the
  specification easier to write" — not "add a new kind of property".
- **Quickstrom** (PLDI 2022) is the one system that does put temporal logic into property-based testing, with
  QuickLTL, a finite-trace LTL dialect where the formula determines how long a trace must be before a verdict is
  possible. It is web-UI specific and in Haskell, but the idea is exactly right.
- **TLA+/TLC, P, Alloy, SPIN, stateright** are the model-checking side: they get completeness and temporal logic,
  but the model lives in a separate language and nothing connects it to the code that ships.
- **Coyote** and **Lincheck** systematically explore *schedules* rather than data, which is a different and
  complementary axis (CsCheck's `SampleParallel` occupies that space).

Two caveats on that, because the first version of this section overclaimed.

**The architecture here is not novel; it is a standard explicit-state model checker.** Rust's
[stateright](https://docs.rs/stateright) has almost exactly this core: `init_states`, `actions(state)`,
`next_state(state, action)`, named `properties()`, and a `within_boundary` predicate to bound the space. On state space
reduction it has an explicit feature this does not — symmetry reduction behind `CheckerBuilder::symmetry`, with a
`Representative` trait — but that is an ergonomic difference rather than a capability one: stateright needs the trait
because its state is opaque to the checker, whereas here `S` is your own type and its `Equals` is the reduction, so the
same collapse is available by writing it. See "Symmetry is just a coarser equality" in [Spec.md](Spec.md). TLA+
correspondences are just as direct — `(before, after)` predicates are primed variables, saturating counters are
bounded model values, putting the observation in the state is a history variable, and breadth first search for a
shortest counterexample is what TLC does. `Terminal` is SPIN's "valid end states". Pure `next_state` plus a
precondition per action is `eqc_statem`.

**What is actually different is narrower.** stateright's properties are stateless predicates over a *single* state —
`condition: fn(&M, &M::State) -> bool` — so they cannot express a transition property or any history, and its
`eventually` documents its own unsoundness on cycles: "eventually properties only work correctly on acyclic paths",
because "the checker does not differentiate cycles from DAG joins", so an unmet obligation on a cycle-closing edge
"will be ignored - a false negative". Here the response deadline is *part of the search state*, so a cycle carrying an
unmet obligation decrements it to expiry and is reported. That soundness on cycles, plus one specification object shared
by the exhaustive engine and a shrinking random engine, is the contribution — not "trace properties and exhaustive
checking", which exist elsewhere.

**And the `Triggered` column is not new either, only newly default.** Reporting that a requirement passed because its
antecedent never fired is vacuity detection, which has been part of the model checking literature since Beer,
Ben-David, Eisner and Rodeh in 1997 and is standard in industrial hardware verification. What is unusual is finding it
in a property-based testing library, printed on every run rather than offered as a separate analysis.

## What this replaces

The alternative for "prove the session layer" is a proof assistant. The comparison that matters is not
expressiveness, it is where the gap ends up:

- A Lean or Coq proof is about a model written in Lean or Coq. The C# that ships is connected to it by hand, or
  not at all. The gap between model and code is exactly where protocol bugs live — and it is unbounded and
  unchecked.
- `Exhaustive` proves a model that is 150 lines of ordinary C#, reviewable by anyone on the team, and
  `Conform` closes the gap to the shipping engine mechanically, on every CI run. The proof is weaker: it holds
  for the abstraction and for bounded counters. But the *end-to-end* claim is stronger, because nothing in the
  chain is done by hand.
- It costs an afternoon rather than a quarter, and it runs in a unit test in under a second, which means it keeps
  running after the person who wrote it has moved on.

Writing the FIX model produced eight findings before it closed, at increasing depth as the shallow ones were fixed
— three of them defects in the design rather than in the requirements, and one a wrong finding the model itself
had caused. Two more came later, from reviewing the model against QuickFIX/n rather than from running it, and they
are the two most worth reading: an abstraction that silently dropped a rule, and a requirement gap that a fault
exclusion had been written around. Four more came from widening the scope afterwards, and those are about the
method: findings 11 to 14 are the ones to read if you are deciding whether this approach is worth adopting.

1. depth 1 — `POSSDUP-IGNORED` contradicted `LOGON-FIRST`: a duplicate before Logon must still drop the
   connection. Requirement too broad.
2. depth 1 — a bug in `Spec.cs` itself: `Rule` with both `on:` and `when:` ignored `when:`.
3. depth 3 — `SEQ-TOO-HIGH-QUEUE` written in units the abstraction cannot express.
4. depth 4 — `LOGOUT-COMPLETES` bound off by one against the model's clock.
5. **depth 6 — the logout timeout was measured from the last message *sent*, so a counterparty that keeps
   eliciting replies keeps a half-closed session alive indefinitely.** This is what QuickFIX's
   `LastSentTimeDT`-based `LogoutTimedOut` actually means.
6. **depth 7 — an unanswered TestRequest was ignored once a Logout was outstanding, so a dead counterparty held
   the socket open longer than a live one.**

7. depth 6 — **the first version of finding 8 below was wrong, and the model was why.** `Receive` reset the
   inbound clock on every message that arrived. QuickFIX assigns `LastReceivedTimeDT` and clears
   `TestRequestCounter` at the *end* of `Verify`, after every early return, so a message that is queued for a gap,
   ignored as a duplicate, or rejected for a bad CompID never refreshes the timers. Splitting the model into
   `Arrive` (off the wire) and `Accept` (passed validation and dispatched) is the fix, and it grew the state space
   from 651 to 983.
8. depth 6 — four requirements tested `RecvSeq` without first checking that a message had arrived, relying on
   `Clock()` resetting `RecvSeq` rather than saying so. `Faults` surfaced it: a fault named
   `test request never times out` came back **caught by `SEQ-TOO-HIGH-QUEUE`**, because it fabricated a state with
   no inbound message but a stale `RecvSeq`. Adding `a.Heard` to the four `when:` clauses fixed it, and the fault
   went back to being caught by `TESTREQ-TIMEOUT`. The `Caught by` column earns its place here: the fault was
   caught, the suite was green, and only the *name* of the catching requirement said anything was wrong.
9. **The abstraction dropped a rule, and the requirement written on top of it was therefore wrong.** A single
   `TooLowDup` case meant "PossDupFlag=Y with MsgSeqNum too low", so `POSSDUP-IGNORED` could only say the message is
   ignored — `a.Sent == Out.None`. But `DoPossDup` sends a Reject when `OrigSendingTime` is missing or later than
   `SendingTime`. The requirement *forbade* what the session layer *requires*, and it passed because the model could
   not express the case. Splitting out `DupBadOrig` and adding `POSSDUP-BAD-ORIG` fixed it. Nothing in the tooling
   catches this class of error: an exhaustive proof is only ever a proof about the abstraction, and whether the
   abstraction preserves the rules is a reading job. It is the reason the header comment on `FixEngineSpec.cs` now lists
   what is out of scope instead of claiming nothing is lost.
10. **A requirement gap that a fault exclusion had been written around.** `SEQ-TOO-LOW-FATAL` is gated on `b.Up`, and
   in `AwaitingLogon` that is false, so nothing covered a Logon arriving with `MsgSeqNum` too low. The fault that
   should have exposed it, `too low is not fatal`, excludes `In.Logon` and is itself gated on `b.Up` — so the hole
   and the fault's blind spot were the same shape, and the table stayed green. Adding `LOGON-TOO-LOW` plus a fault
   that can actually reach the case catches it **at 1 step**. A fault suite is a specification too, and its
   exclusions deserve the same suspicion as the requirements'.

Extending the model to the outbound sequence number produced four more, and they are about the *method* rather than
about FIX. The scope had been "session establishment, inbound sequencing and liveness"; the outbound counter and a
session that outlives its connection were the smallest honest widening of it.

11. **A requirement can be unfalsifiable and still look fine.** `RESET-RESETS-BOTH` says `ResetSeqNumFlag=Y` resets
    both directions — `MemoryStore.Reset` sets `NextSenderMsgSeqNum` and `NextTargetMsgSeqNum` to 1. It triggered,
    and it was worthless: a Logon can only arrive before logon, where both counters are already 1, so "resets both"
    and "resets only the inbound side" produce identical states. The fault written for it would have escaped. Making
    it mean anything needed `Disconnected` to stop being the end of the trace and one `Reconnect` to carry the
    numbers across — after which the fault is caught at 3 steps, the first two spent getting a counter above 1.
    `Faults` is what turns this from an invisible problem into a failing test.
12. **Adding one field re-attributed an existing fault.** `no heartbeat when idle` suppressed the heartbeat but still
    consumed an outbound number, so it became a violation of the new arithmetic requirement and was caught by
    `OUTBOUND-ADVANCES` instead of `HB-KEEPALIVE`. Every fault still had a catcher and the suite was green; only the
    `Caught by` column showed that `HB-KEEPALIVE` had quietly stopped being proven. Two attributions are now asserted
    rather than merely printed. This is the second time that column has caught a regression the assertions missed.
13. **`Precedes` ran out, and a scope parameter turned out not to be the answer.** `NO-APP-BEFORE-LOGON` is a
    whole-trace claim: once any Logon has been sent it is satisfied forever, including on a later connection that has
    not logged on yet. The `app sent before the second logon` fault is the same defect one connection later and it does
    not see it. What the requirement means is "before logon *on this connection*", so this looked like the case that
    justified adding scopes. It is not, for a reason worth knowing: the trace *begins inside the scope*. A scope arms on
    its opening event, and no transition opens the first connection because `AwaitingLogon` is the initial state, so
    `NeverAfter(until:)` would cover every connection but the first. No single scoped form covers both, which is why the
    example still states it twice, once as whole-trace history and once as a `Never` over the state. Keeping both is
    worth more than replacing one: side by side they show exactly what a history form does and does not buy.
14. **`Conform` compares state, not requirements — and reports one counterexample.** The two `Tick` implementations
    disagreed: the specification tests an unanswered TestRequest before the logout timeout, the engine tested it
    after, so from `LogoutSent` with a TestRequest outstanding one terminated and the other aged. Five steps, and
    invisible, because `Conform` shrinks to the *shortest* divergence and the engine's deliberately planted
    `TooLowDup` defect is reachable in two. A planted defect masks every divergence behind it. The deeper point is
    that nothing exhaustively relates the engine to the model: `Exhaustive` proves requirements about the model,
    `Conform` samples state equality against the engine, and a requirement proved on one is not thereby true of the
    other. Found by reading the two files side by side, which is not a method that scales.

And one finding that is not a defect but a real property of the implementation, kept as a test that asserts the
counterexample still exists: **nothing bounds how long a gap may stay open.** `NextSequenceReset` calls
`Verify(msg, isGapFill, isGapFill)`, so a bare SequenceReset-Reset (GapFillFlag=N) is verified with *both* sequence
checks off. It therefore passes `Verify` and refreshes the liveness timers before `NewSeqNo` is even looked at, and
is only then rejected as too low. A counterparty trickling invalid SequenceResets keeps the inbound clock fresh
with messages that do nothing at all, so the test-request timeout never fires and the gap is never filled:

```
     1 Recv(Logon TooHigh)     LoggedOn exp=1 gap+1 quiet=0   >> Logon, ResendRequest
     2 Tick                    LoggedOn exp=1 gap+1 quiet=1
     3 Recv(SeqReset TooLow)   LoggedOn exp=1 gap+1 quiet=0   >> Reject      <- free liveness refresh
     4 Tick                    LoggedOn exp=1 gap+1 quiet=1
     5 Tick                    LoggedOn exp=1 gap+1 quiet=2
 >>  6 Tick                    LoggedOn exp=1 gap+1 quiet=3 tr?  >> TestRequest
```

Note this one is about QuickFIX/n's `Verify` ordering, not about the FIX specification, which does not dictate it.

## What the worked examples changed

The modelling techniques these produced are in [Spec.md](Spec.md#abstraction-is-the-whole-skill). What follows is
what writing them changed about the library and about the designs being specified.

### The cache

[`Tests/Specs/RefreshCacheSpec.cs`](../Tests/Specs/RefreshCacheSpec.cs) specifies a refresh-on-access cache: two keys, a TTL, and
single-flight loading. It was written to stress the axes FIX did not — structural state, concurrency, and a
specification that is a design decision rather than a document — and it closes at 1,445 states and 6,713
transitions at depth 22.

Three things the example changed:

1. **It found a missing requirement form.** "Once a key has been loaded, no later read of it misses" is
   `once P, thereafter never Q` — the mirror of `Precedes`, and there was no way to say it. `NeverAfter` is now in
   the library: same one bit of history, opposite test.
2. **It found that the history-carrying forms were not parameterised.** The first `NeverAfter` used one global bit,
   so loading key A discharged the obligation for a read of key B — a three step counterexample. All three
   history-carrying forms now take an optional domain and register one instance per element, reported as `id[A]`,
   `id[B]`. `per:` keeps the limitation, because it names an action rather than an action and its argument, and no
   example has needed otherwise: split the action into separately named actions if yours does.
3. **It showed where liveness stops being yours.** This cache has *no* provable liveness property. A value only
   refreshes if something reads it and the loader returns, and the cache bounds neither. Every `Response` first
   written for it was really an assertion about the loader's latency, which is the same mistake as `GAP-RESOLVED`
   in the FIX model. The specification therefore has no `Response` at all, and a separate test asserts the property
   an engineer would assume, so the counterexample is on the record. If you do have an environment-relative bound,
   `per:` is how to say it: measure the deadline in units of the environment action you depend on.

The `Faults` `Caught by` column earned its keep three more times. `one lock over the whole cache` was caught by
`MISS-STARTS-LOAD` until the fault stopped modelling a blocked read as a miss; `a completing load writes an older
value` was caught by `COMPLETE-IS-FRESH` until it was clamped to keep the value present; and an invariant that the
load count never goes negative was caught by nothing at all, because the action guards make it structurally true —
it was a check on the encoding dressed as a requirement, and it is gone. In all three cases the suite was green and
only the name of the catching requirement said anything was wrong.

`Conform` also finally has a clean demonstration. [`Tests/Specs/RefreshCache.cs`](../Tests/Specs/RefreshCache.cs) has no
planted defect, so the conformance run completes with every requirement triggered over ~172,000 reads rather than
stopping at the first divergence the way the FIX engine does.
### The lease: using it to choose a design

[`Tests/Specs/FencingSpec.cs`](../Tests/Specs/FencingSpec.cs) specifies a distributed lease and the resource it protects — two clients,
a lock service with expiry, and a store — in **three configurations of one specification**. The first two do not
satisfy their own safety requirement. That makes this the first example where the tool is used to *choose* a design
rather than to find a bug in one.

The argument is Martin Kleppmann's: a lease must expire or a crashed client holds the lock forever, but nothing
bounds the delay between a client checking that it holds the lease and its write landing. So the property that
matters is not "one client holds the lock record" but "one client is mutating the resource".

| Configuration | Result |
|---|---|
| `Fence.None` — plain lease | `NO-LOST-UPDATE` fails at 6 steps |
| `Fence.Writes` — token checked on writes | `SUPERSEDED-TOKEN-REFUSED[1]` fails at 5 steps |
| `Fence.Every` — token on every access | **closed**: 583 states, 3,022 transitions, depth 10 |

**Fencing only the writes is not enough**, which was not the result I expected. The resource learns a token only
when one is presented, so a new holder that has not written yet leaves the *old* holder's token still the highest
the resource has seen, and the old holder's late write is accepted. The fix in the model is to present the token on
reads too; pushing the token from the lock service at grant time would work equally well. This is a conclusion from
the model, not a quote from the article, which does not say whether reads carry the token.

And one requirement worth copying: `LIVE-HOLDER-NEVER-REFUSED`, which says a client that really does hold the lease
is never refused. Without it, a resource that rejected every write would satisfy the safety requirement perfectly.
Every safety property needs its anti-degenerate twin, and `Faults` is what shows the twin is live — here it catches
a resource comparing tokens with `<=` at three steps.

`Faults` also retired a fault. A client reusing its previous token came back **caught by NOTHING**, and it was
right: a reused token is either below the fence and refused, or equal to it and harmless. The hypothesised bug was
not one. The lock service reissuing a token is the real defect, and that is caught at six steps.
