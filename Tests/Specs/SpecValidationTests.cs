namespace Tests.Specs;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsCheck;

/// <summary>The mistakes a specification can contain that would otherwise pass silently and prove nothing. Each of
/// these is rejected before any exploration starts, because the failure mode is a green test rather than a red one:
/// a requirement that never fires, or a coverage row credited to the wrong requirement.</summary>
public class SpecValidationTests
{
    static Spec<int> Counter() => Spec.From(0).Action("Inc", i => i + 1);

    [Test]
    public async Task Duplicate_Requirement_Id_Is_Rejected()
    {
        var spec = Counter()
            .Invariant("SAME", "first", i => i >= 0)
            .Invariant("SAME", "second", i => i < 100);
        var message = Assert.Throws<CsCheckException>(() => spec.Exhaustive(maxStates: 10))!.Message;
        await Assert.That(message).Contains("SAME");
    }

    /// <summary>Two actions with the same name would cause on: and per: requirements to silently cover only the first,
    /// leaving the second invisible to scoped requirements.</summary>
    [Test]
    public async Task Duplicate_Action_Name_Is_Rejected()
    {
        var spec = Spec.From(0).Action("Inc", i => i + 1).Action("Inc", i => i + 2);
        var message = Assert.Throws<CsCheckException>(() => spec.Exhaustive(maxStates: 10))!.Message;
        await Assert.That(message).Contains("Inc");
    }

    /// <summary>If the spec already violates a requirement without any fault injected, every mutation would appear
    /// "caught" regardless of whether it caused anything. Faults detects this and fails before running mutations.</summary>
    [Test]
    public async Task Faults_Rejects_A_Spec_That_Already_Fails()
    {
        var spec = Spec.From(0).Action("Inc", i => i < 4, i => i + 1)
            .Never("NO-TWO", "the counter never reaches two", (_, a) => a == 2)
            .Fault("irrelevant", (_, _) => false, (_, a) => a);
        var message = Assert.Throws<CsCheckException>(() => spec.Faults())!.Message;
        await Assert.That(message).Contains("NO-TWO");
    }

    /// <summary>SampleFaults also checks the baseline before injecting faults. The baseline for SampleFaults uses a
    /// sampled walk rather than Exhaustive, so it is cost-proportional and checks exactly the traces that the fault
    /// walks will later sample, closing the gap without paying for a full exhaustive search.</summary>
    [Test]
    public async Task SampleFaults_Rejects_A_Spec_That_Already_Fails()
    {
        var spec = Counter()
            .Never("NO-TWO", "the counter never reaches two", (_, a) => a == 2)
            .Fault("irrelevant", (_, _) => false, (_, a) => a);
        var message = Assert.Throws<CsCheckException>(() => spec.SampleFaults())!.Message;
        await Assert.That(message).Contains("NO-TWO");
    }

    /// <summary>A fault on a spec whose space does not close is inconclusive, not uncaught. If Faults gives up at
    /// maxStates without finding a violation, it cannot claim the fault is undetectable; it only explored part of
    /// the space. The fault shows as NOT CLOSED in the table and appears in Inconclusive rather than Uncaught, so
    /// throwOnUncaught does not fire and the caller knows the result is not a proof.</summary>
    [Test]
    public async Task Faults_Reports_Inconclusive_When_Search_Does_Not_Close()
    {
        // Counter() is unbounded so Exhaustive gives up at maxStates without closing.
        var report = Counter()
            .Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Fault("a jump", (_, a) => a == 3, (_, a) => a + 1)
            .Faults(maxStates: 5, throwOnUncaught: false);
        await Assert.That(report.Uncaught).IsEmpty();
        await Assert.That(report.Inconclusive).Contains("a jump");
        await Assert.That(report.ToString()).Contains("NOT CLOSED");
        await Assert.That(report.Results[0].Outcome).IsEqualTo(FaultOutcome.Inconclusive);
    }

    [Test]
    public async Task Unknown_On_Action_Is_Rejected()
    {
        var spec = Counter().Rule("R", "quote", on: "Increment", then: (b, a) => a > b);
        var message = Assert.Throws<CsCheckException>(() => spec.Exhaustive(maxStates: 10))!.Message;
        await Assert.That(message).Contains("Increment");
    }

    [Test]
    public async Task Unknown_Per_Action_Is_Rejected()
    {
        var spec = Counter().Response("R", "quote", (_, a) => a == 1, (_, a) => a > 1, within: 2, per: "Clock");
        var message = Assert.Throws<CsCheckException>(() => spec.Exhaustive(maxStates: 10))!.Message;
        await Assert.That(message).Contains("Clock");
    }

    /// <summary>A Spec is frozen once an engine has run it. Without this, a Spec held in a static field and added to
    /// by one test would silently change what every other test checked, and the order the tests happened to run in
    /// would decide the result.</summary>
    [Test]
    public async Task Adding_To_A_Spec_After_Running_It_Is_Rejected()
    {
        var spec = Counter().Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0);
        spec.Exhaustive(maxStates: 10);
        var message = Assert.Throws<CsCheckException>(
            () => spec.Invariant("LATE", "added too late", i => i < 5))!.Message;
        await Assert.That(message).Contains("fresh Spec");
    }

    /// <summary>Deriving a variant from a fresh Spec is the supported pattern, and is what every example does.</summary>
    [Test]
    public async Task Deriving_A_Variant_From_A_Fresh_Spec_Is_Fine()
    {
        Counter().Invariant("A", "one", i => i >= 0).Exhaustive(maxStates: 10);
        var report = Counter().Invariant("A", "one", i => i >= 0)
                              .Invariant("B", "two", i => i < 1000)
                              .Exhaustive(maxStates: 10);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>An unreachable Reachable requirement is only a failure once the space has closed, because that is the
    /// only point at which unreachability can be concluded rather than guessed.</summary>
    [Test]
    public async Task Unreachable_Requirement_Fails_Once_The_Space_Closes()
    {
        var spec = Spec.From(0)
            .Action("Inc", i => i < 3, i => i + 1)
            .Reachable("TEN", "the counter can reach ten", i => i == 10);
        spec.Exhaustive(out var violation);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("TEN");
        await Assert.That(violation.Detail).Contains("unreachable");
    }

    /// <summary>The same specification with the bound raised: the state is now reachable and nothing is reported.</summary>
    [Test]
    public async Task Reachable_Requirement_Passes_When_The_State_Is_Reached()
    {
        var report = Spec.From(0)
            .Action("Inc", i => i < 10, i => i + 1)
            .Reachable("TEN", "the counter can reach ten", i => i == 10)
            .Exhaustive();
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>A state carrying a value the behaviour never reads: Tag counts up without bound, and Equals and
    /// GetHashCode are written to ignore it.</summary>
    readonly record struct Tagged(int Step, int Tag)
    {
        public bool Equals(Tagged other) => Step == other.Step;
        public override int GetHashCode() => Step;
    }

    [Test]
    public async Task State_Equality_Decides_What_Counts_As_A_Distinct_State()
    {
        var report = Spec.From(new Tagged(0, 0))
            .Action("Step", t => t.Step < 3, t => new Tagged(t.Step + 1, t.Tag + 1))
            .Action("Churn", t => t with { Tag = t.Tag + 1 })
            .Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.States).IsEqualTo(4);
    }

    readonly record struct Wide(int Counter, bool Flag, int Small);

    /// <summary>"Gave up at a million states" tells you nothing actionable, and finding the unbounded field by reading
    /// the model is the main cliff in using any of this. The note now names the widest fields, sampled from the states
    /// already reached, so the fix points at itself. Counter is unbounded, Flag has two values and Small three.</summary>
    [Test]
    public async Task Not_Closing_Names_The_Widest_State_Field()
    {
        var report = Spec.From(new Wide(0, false, 0))
            .Action("Grow", w => w with { Counter = w.Counter + 1 })
            .Action("Flip", w => w with { Flag = !w.Flag })
            .Action("Bump", w => w with { Small = (w.Small + 1) % 3 })
            .Exhaustive(maxStates: 2_000, writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsFalse();
        await Assert.That(report.Note).Contains("widest state fields are Counter");
        await Assert.That(report.Note!.IndexOf("Counter", StringComparison.Ordinal))
            .IsLessThan(report.Note!.IndexOf("Small", StringComparison.Ordinal));
        await Assert.That(report.Note).Contains("Flag (2 values)");
    }

    /// <summary>A state with a hand written ToString cannot be parsed into fields, so the diagnostic is omitted rather
    /// than guessed at, and the rest of the note is unchanged. This model is also honestly infinite rather than badly
    /// abstracted, so it never revisits a state - which is why that clause of the note has to read as something to
    /// check rather than as a diagnosis.</summary>
    [Test]
    public async Task Widest_Field_Diagnostic_Is_Omitted_When_It_Cannot_Parse()
    {
        var report = Spec.From(0).Action("Inc", i => i + 1).Exhaustive(maxStates: 100, writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsFalse();
        await Assert.That(report.Note).Contains("gave up at 100 states");
        await Assert.That(report.Note).DoesNotContain("widest");
        await Assert.That(report.Note).Contains("no state was ever revisited");
    }

    /// <summary>A model that only ever advances is legitimately a tree, so the note that observes it must not read as an
    /// accusation. This eleven state chain has perfect value equality, and a chain is the first model anyone writes.
    /// A model that does revisit says nothing at all, which is what keeps the note a signal rather than boilerplate.</summary>
    [Test]
    public async Task A_Tree_Shaped_Space_Is_Observed_Not_Blamed()
    {
        var chain = Spec.From(0).Action("Inc", i => i < 10, i => i + 1).Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(chain.Closed).IsTrue();
        await Assert.That(chain.States).IsEqualTo(11);
        await Assert.That(chain.Revisits).IsEqualTo(0);
        await Assert.That(chain.Note).Contains("the reachable space is a tree");
        await Assert.That(chain.Note).Contains("expected if the model only advances");

        var cycle = Spec.From(0).Action("Tick", i => (i + 1) % 12).Exhaustive();
        await Assert.That(cycle.Closed).IsTrue();
        await Assert.That(cycle.States).IsEqualTo(12);
        await Assert.That(cycle.Revisits).IsEqualTo(1);
        await Assert.That(cycle.Note).IsNull();
    }

    /// <summary>An unguarded counter has no finite state space, so maxStates is the only thing that stops it and the
    /// run proves nothing. A Boundary closes it instead over a region chosen by the specification.</summary>
    [Test]
    public async Task Boundary_Closes_A_Space_That_Would_Otherwise_Run_Forever()
    {
        var unbounded = Spec.From(0).Action("Inc", i => i + 1).Exhaustive(maxStates: 500);
        await Assert.That(unbounded.Closed).IsFalse();
        await Assert.That(unbounded.Note).Contains("gave up");

        var report = Spec.From(0).Action("Inc", i => i + 1).Boundary(i => i <= 5).Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.States).IsEqualTo(6);
        await Assert.That(report.Pruned).IsEqualTo(1);
        // The line quoted in docs/Spec.md, asserted here so the two cannot drift.
        await Assert.That(report.ToString()).Contains(
            "state space CLOSED within boundary: 6 states, 6 transitions, depth 5, 0 terminal, 0 deadlock, 1 outside");
    }

    /// <summary>The property that makes a boundary worth having rather than just a smaller maxStates: the step that
    /// leaves the boundary is still checked. Only the expansion of what it reached is given up, so a requirement the
    /// exit transition violates is still found.</summary>
    [Test]
    public async Task Boundary_Still_Checks_The_Step_That_Leaves_It()
    {
        Spec.From(0)
            .Action("Inc", i => i + 1)
            .Boundary(i => i <= 5)
            .Never("NO-SIX", "the counter never reaches six", (_, a) => a == 6)
            .Exhaustive(out var violation);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NO-SIX");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(6);
    }

    /// <summary>And the conclusion a boundary is not allowed to support. Unreachability follows from closure, and a
    /// pruned space has not closed over everything, so this reports a note instead of a violation.</summary>
    [Test]
    public async Task Boundary_Does_Not_Let_Reachable_Be_Called_Unreachable()
    {
        var report = Spec.From(0)
            .Action("Inc", i => i + 1)
            .Boundary(i => i <= 5)
            .Reachable("TEN", "the counter can reach ten", i => i == 10)
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNull();
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.Note).Contains("TEN");
        await Assert.That(report.Note).Contains("not a failure");
    }

    /// <summary>A boundary that excludes the initial state would prune everything and report one state, closed - which
    /// reads exactly like a proof.</summary>
    [Test]
    public async Task Boundary_Excluding_The_Initial_State_Is_Rejected()
    {
        var spec = Spec.From(0).Action("Inc", i => i + 1).Boundary(i => i > 3);
        var message = Assert.Throws<CsCheckException>(() => spec.Exhaustive())!.Message;
        await Assert.That(message).Contains("initial state");
    }

    /// <summary>The claim that makes bounded liveness sound here rather than best effort: an outstanding obligation is
    /// part of the search state, so a cycle cannot discharge it by revisiting a state.
    /// <para>This model has exactly two concrete states and Tick cycles between them, so the whole space is visited in two
    /// steps. If the deadline were not part of the node key the frontier would empty with the obligation still owed
    /// and nothing would be reported - which is precisely the unsoundness stateright documents for its eventually.
    /// The violation being found at all is the property; the depth shows the deadline counting down across the</para>
    /// cycle.</summary>
    [Test]
    public async Task Response_Obligation_Survives_A_Cycle()
    {
        Spec.From(0)
            .Action("Tick", i => (i + 1) % 2)
            .Response("NEVER-SETTLES", "entering one must be followed by settling",
                trigger: (b, a) => b == 0 && a == 1,
                response: (_, _) => false,
                within: 3, per: "Tick")
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NEVER-SETTLES");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(4);
    }

    /// <summary>An Invariant already false before anything has happened has its own path: there is no transition to
    /// blame, so the violation carries an empty trace and a step index of -1, and there is no state space to describe.
    /// Every other violation test here goes through a step.</summary>
    [Test]
    public async Task Invariant_False_In_The_Initial_State_Is_Reported()
    {
        var report = Counter()
            .Invariant("POSITIVE", "the counter is always positive", i => i > 0)
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("POSITIVE");
        await Assert.That(violation.Detail).Contains("initial state");
        await Assert.That(violation.StepIndex).IsEqualTo(-1);
        await Assert.That(violation.Trace.Steps).IsEmpty();
        await Assert.That(report.States).IsEqualTo(0);

        // The random engine reaches it through its own code path, and throws by default like the proof does.
        var message = Assert.Throws<CsCheckException>(() => Counter()
            .Invariant("POSITIVE", "the counter is always positive", i => i > 0).Sample())!.Message;
        await Assert.That(message).Contains("POSITIVE");
    }

    /// <summary>cancel: discharges an obligation that was never going to be answered - a request withdrawn, a session
    /// dropped before its confirming Logout. Elsewhere it is only exercised incidentally, and the way for it to be
    /// wrong is to be ignored, which looks exactly like a passing test. So the same model is checked both ways.</summary>
    [Test]
    public async Task Response_Cancel_Discharges_The_Obligation()
    {
        // One is pending and two is its only successor, so without a cancel the deadline always expires.
        static Spec<int> Waiting(Func<int, int, bool>? cancel) => Spec.From(0)
            .Action("Raise", i => i == 0, _ => 1)
            .Action("Abandon", i => i == 1, _ => 2)
            .Response("ANSWERED", "a raised request is answered",
                trigger: (_, a) => a == 1, response: (_, a) => a == 3, within: 1, cancel: cancel);

        Waiting(null).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("ANSWERED");

        var report = Waiting((_, a) => a == 2).Exhaustive(out var none, TUnitX.WriteLine);
        await Assert.That(none).IsNull();
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>The other side of Faults_Throws_When_A_Defect_Escapes. With throwOnUncaught off an escape is a value to
    /// read rather than an exception, so a whole table can be triaged at once instead of one fault per run.</summary>
    [Test]
    public async Task Faults_Reports_An_Uncaught_Defect_Without_Throwing()
    {
        var report = Spec.From(0)
            .Action("Inc", i => i < 5, i => i + 1)
            .Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Fault("counter advances twice", (_, _) => true, (_, a) => a + 1)
            .Fault("counter goes negative", (_, _) => true, (_, _) => -1)
            .Faults(TUnitX.WriteLine, throwOnUncaught: false);
        await Assert.That(report.Uncaught.Count).IsEqualTo(1);
        await Assert.That(report.Uncaught).Contains("counter advances twice");
        await Assert.That(report.CaughtBy("counter advances twice")).IsNull();
        await Assert.That(report.CaughtBy("counter goes negative")).IsEqualTo("NON-NEGATIVE");
        await Assert.That(report.ToString()).Contains("NOTHING");
    }

    /// <summary>The same escape reported by the sampled engine, where it means something weaker: no requirement was
    /// seen to detect the fault rather than that none can. The caveat line is in the table so the difference is on the
    /// page next to the NOTHING it qualifies, and the exception says "in the walks sampled" for the same reason.</summary>
    [Test]
    public async Task SampleFaults_Reports_An_Uncaught_Defect()
    {
        var spec = Spec.From(0)
            .Action("Inc", i => i < 5, i => i + 1)
            .Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Fault("counter advances twice", (_, _) => true, (_, a) => a + 1)
            .Fault("counter goes negative", (_, _) => true, (_, _) => -1);
        var report = spec.SampleFaults(TUnitX.WriteLine, iter: 500, throwOnUncaught: false);
        await Assert.That(report.CaughtBy("counter advances twice")).IsNull();
        await Assert.That(report.CaughtBy("counter goes negative")).IsEqualTo("NON-NEGATIVE");
        await Assert.That(report.ToString()).Contains("not that none exists");

        var message = Assert.Throws<CsCheckException>(() => spec.SampleFaults(iter: 500))!.Message;
        await Assert.That(message).Contains("in the walks sampled");
    }

    /// <summary>minSteps is the floor on generated trace length, so a property that only shows up after several steps is
    /// not drowned in traces too short to reach it. The way for it to be wrong is to be ignored, which nothing else here
    /// would notice. Checked on the generator, where lengths are visible, and through Sample, where the plumbing is.</summary>
    [Test]
    public async Task MinSteps_Sets_The_Shortest_Trace_Generated()
    {
        int shortest = int.MaxValue, longest = 0;
        Spec.From(0).Action("Inc", i => i + 1).GenTrace(minSteps: 7, maxSteps: 9)
            .Sample(trace =>
            {
                shortest = Math.Min(shortest, trace.Steps.Length);
                longest = Math.Max(longest, trace.Steps.Length);
            }, iter: 2_000, threads: 1);
        await Assert.That(shortest).IsEqualTo(7);
        await Assert.That(longest).IsEqualTo(9);

        var report = Counter().Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Sample(TUnitX.WriteLine, minSteps: 7, maxSteps: 9, iter: 500);
        await Assert.That(report.TracesWalked).IsEqualTo(500);
        await Assert.That(report.StepsWalked >= 500 * 7).IsTrue();
        await Assert.That(report.StepsWalked <= 500 * 9).IsTrue();
    }

    /// <summary>Response is about the steps after the trigger: a response holding on the trigger step itself does not
    /// discharge the obligation. Pinned because it is stricter than the usual reading of leads-to and the opposite of
    /// Precedes, so a same-step property written as a Response produces a counterexample that reads like a tool bug.
    /// Such a property is a Rule, which is how the worked examples state theirs.</summary>
    [Test]
    public async Task Response_Is_Not_Discharged_On_The_Trigger_Step()
    {
        Spec.From(0)
            .Action("Go", i => i < 4, i => i + 1)
            .Response("SAME-STEP", "reaching one is answered by reaching one",
                trigger: (_, a) => a == 1, response: (_, a) => a == 1, within: 1)
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("SAME-STEP");

        // Precedes, the other order form, is satisfied by one step holding both - "at or before it".
        var report = Spec.From(0)
            .Action("Go", i => i < 4, i => i + 1)
            .Precedes("SAME-STEP", "reaching one is preceded by reaching one",
                first: (_, a) => a == 1, second: (_, a) => a == 1)
            .Exhaustive(out var none);
        await Assert.That(none).IsNull();
        await Assert.That(report.Closed).IsTrue();
    }

    /// <summary>Bounded existence: "at most three retries". Three are allowed and the fourth is a violation, reported
    /// on the step that crosses the bound rather than at the end.</summary>
    [Test]
    public async Task AtMost_Allows_The_Bound_And_Fails_The_Next()
    {
        static Spec<int> Retries(int allowed) => Spec.From(0)
            .Action("Retry", i => i < 4, i => i + 1)
            .AtMost("AT-MOST-THREE", "at most three retries", allowed, (b, a) => a > b);

        var ok = Retries(4).Exhaustive();
        await Assert.That(ok.Closed).IsTrue();

        Retries(3).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("AT-MOST-THREE");
        await Assert.That(violation.Detail).Contains("more than 3 times");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(4);
    }

    /// <summary>The same soundness question as Response on a cycle, and the reason the count is in the search node
    /// rather than a tally. Tick cycles between two states, so the whole space is two states and a count kept outside
    /// the node would be discharged by revisiting one. The occurrences here are unbounded, so the bound must be
    /// crossed on some path and the violation must be found.</summary>
    [Test]
    public async Task AtMost_Counts_Across_A_Cycle()
    {
        Spec.From(0)
            .Action("Tick", i => (i + 1) % 2)
            .AtMost("AT-MOST-TWICE", "at most twice", 2, (b, a) => b == 0 && a == 1)
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("AT-MOST-TWICE");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(5);
    }

    /// <summary>Zero occurrences satisfies an AtMost, but silently, so it is reported as a NEVER in the coverage table
    /// the way the other guarded forms are - otherwise "at most three retries" passes on a model that cannot retry.</summary>
    [Test]
    public async Task AtMost_Reports_Vacuity_When_It_Never_Occurs()
    {
        var report = Spec.From(0)
            .Action("Stay", i => i)
            .AtMost("AT-MOST-THREE", "at most three retries", 3, (b, a) => a > b)
            .Exhaustive();
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).Contains("AT-MOST-THREE");
    }

    /// <summary>A Rule with neither on: nor when: is the transition counterpart of an Invariant, and reports every step
    /// rather than a count - the count a when: of true produces looks like vacuity information but is only the number of
    /// steps evaluated, which is what this overload exists to stop.</summary>
    [Test]
    public async Task Rule_Over_Every_Step_Reports_Every_Step()
    {
        var report = Spec.From(0)
            .Action("Inc", i => i < 4, i => i + 1)
            .Rule("ADVANCES", "every step advances the counter by one", (b, a) => a == b + 1)
            .Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.ToString()).Contains("| ADVANCES    |  every step |");

        // The same claim with a when: of true is guarded, so it reports a number that means the same thing less clearly.
        var guarded = Spec.From(0)
            .Action("Inc", i => i < 4, i => i + 1)
            .Rule("ADVANCES", "every step advances the counter by one", (_, _) => true, (b, a) => a == b + 1)
            .Exhaustive();
        await Assert.That(guarded.ToString()).Contains("| ADVANCES    |           4 |");

        Spec.From(0)
            .Action("Inc", i => i < 4, i => i + 1)
            .Action("Jump", i => i == 0, _ => 2)
            .Rule("ADVANCES", "every step advances the counter by one", (b, a) => a == b + 1)
            .Exhaustive(out var violation);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Detail).Contains("does not hold over the step");
    }

    /// <summary>Step numbers were padded to two characters, so a trace of a hundred steps or more lost its alignment.
    /// The state lines follow the width so they stay level with the action names.</summary>
    [Test]
    public async Task Long_Traces_Stay_Aligned()
    {
        var lines = Walk(120).ToString().Split('\n');
        await Assert.That(lines.Any(l => l.StartsWith("    120 Inc", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(l => l.StartsWith("      1 Inc", StringComparison.Ordinal))).IsTrue();
        // Action name and state both start in column 10 for a three digit trace.
        await Assert.That(lines.Count(l => l.StartsWith("          ", StringComparison.Ordinal))).IsEqualTo(121);

        await Assert.That(Walk(3).ToString().Split('\n')
            .Any(l => l.StartsWith("     1 Inc", StringComparison.Ordinal))).IsTrue();

        // The step count is pinned by minSteps and maxSteps, so any generated trace has exactly the length under test.
        static Trace<int> Walk(int steps)
        {
            Trace<int> trace = null!;
            Spec.From(0).Action("Inc", i => i + 1).GenTrace(minSteps: steps, maxSteps: steps)
                .Sample(t => trace = t, iter: 1, threads: 1);
            return trace;
        }
    }

    /// <summary>Four positions and two laps, so a scope opened on the first lap can be shown to have closed before the
    /// second one opens it again.</summary>
    readonly record struct Lap(int Pos, int Count);

    /// <summary>Dwyer's After-Until scope, the one cell of his catalogue these forms were missing: Precedes is already
    /// Absence Before and NeverAfter is Absence After, but nothing said "not between one thing and the next, every time
    /// round". Pos 1 opens the scope and Pos 3 closes it, twice.
    /// <para>The three cases are only conclusive together. Closed proves until closes the scope, because the same predicate
    /// with no until is a violation. Reopened then proves it reopens, because closed has already established that the</para>
    /// scope was shut when the second lap began, so nothing else can explain a violation inside it.</summary>
    [Test]
    public async Task NeverAfter_Until_Closes_The_Scope_And_Reopens_It()
    {
        static Spec<Lap> Scoped(Func<Lap, Lap, bool>? until, Func<Lap, Lap, bool> never) => Spec.From(new Lap(0, 0))
            .Action("Step", l => l.Count < 2, l => l.Pos == 3 ? new Lap(0, l.Count + 1) : l with { Pos = l.Pos + 1 })
            .NeverAfter("SCOPED", "never between position one and position three",
                after: (_, a) => a.Pos == 1, never: never, until: until);

        static bool ClosingStep(Lap _, Lap a) => a.Pos == 0 && a.Count == 1;   // reached from Pos 3, which closed it
        static bool InsideSecond(Lap _, Lap a) => a.Pos == 2 && a.Count == 1;  // reached from Pos 1, which reopened it

        var closed = Scoped((_, a) => a.Pos == 3, ClosingStep).Exhaustive(out var none, TUnitX.WriteLine);
        await Assert.That(none).IsNull();
        await Assert.That(closed.Closed).IsTrue();

        Scoped(null, ClosingStep).Exhaustive(out var unscoped);
        await Assert.That(unscoped).IsNotNull();
        await Assert.That(unscoped!.Detail).Contains("after the point");

        Scoped((_, a) => a.Pos == 3, InsideSecond).Exhaustive(out var reopened, TUnitX.WriteLine);
        await Assert.That(reopened).IsNotNull();
        await Assert.That(reopened!.Id).IsEqualTo("SCOPED");
        await Assert.That(reopened.Detail).Contains("between the step that opens");
        await Assert.That(reopened.Trace.Steps.Length).IsEqualTo(6);

        // The over overload carries until through to each element's own scope.
        var keyed = Spec.From(new Lap(0, 0))
            .Action("Step", l => l.Count < 2, l => l.Pos == 3 ? new Lap(0, l.Count + 1) : l with { Pos = l.Pos + 1 })
            .NeverAfter("SCOPED", "never between position one and position three", [0, 1],
                after: (_, a, c) => a.Pos == 1 && a.Count == c,
                never: (_, a, c) => a.Pos == 0 && a.Count == c + 1,
                until: (_, a, c) => a.Pos == 3 && a.Count == c)
            .Exhaustive(out var keyedViolation);
        await Assert.That(keyedViolation).IsNull();
        await Assert.That(keyed.ToString()).Contains("SCOPED[1]");
    }

    /// <summary>The deadline and history bits are packed into two ulongs, so exceeding the counts would silently
    /// corrupt a proof rather than fail. Each limit is checked, including through the `over` overloads, which spend one
    /// slot per element and so are the easy way to cross a limit without noticing.</summary>
    [Test]
    public async Task Requirement_Limits_Are_Enforced()
    {
        // Response and AtMost draw byte slots from one pool of sixteen spanning both counter words, so the budget can be
        // spent in any mix. Twelve Responses and no AtMost is legal, which eight-of-each would have refused.
        static Spec<int> Responses(int n)
        {
            var spec = Counter();
            for (int i = 0; i < n; i++)
                spec.Response($"R{i}", "quote", (_, a) => a == 1, (_, a) => a > 1, within: 2, per: "Inc");
            return spec;
        }
        Responses(12).GenTrace(1, 1);
        await Assert.That(Assert.Throws<CsCheckException>(() => Responses(17))!.Message)
            .Contains("limit of 16 Response and AtMost");

        // And mixed, which is the case the split budget could not express at all.
        static Spec<int> Mixed(int responses, int atMosts)
        {
            var spec = Responses(responses);
            for (int i = 0; i < atMosts; i++) spec.AtMost($"A{i}", "quote", 3, (b, a) => a > b);
            return spec;
        }
        Mixed(10, 6).GenTrace(1, 1);
        await Assert.That(Assert.Throws<CsCheckException>(() => Mixed(10, 7))!.Message)
            .Contains("limit of 16 Response and AtMost");

        static Spec<int> SeventeenOver() => Counter().Response("R", "quote",
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17],
            (_, a, t) => a == t, (_, a, t) => a > t, within: 2, per: "Inc");
        await Assert.That(Assert.Throws<CsCheckException>(() => SeventeenOver())!.Message)
            .Contains("limit of 16 Response and AtMost");

        await Assert.That(Assert.Throws<CsCheckException>(
            () => Counter().Response("R", "q", (_, _) => true, (_, _) => true, within: 0, per: "Inc"))!.Message)
            .Contains("within must be 1 to 254");
        await Assert.That(Assert.Throws<CsCheckException>(
            () => Counter().Response("R", "q", (_, _) => true, (_, _) => true, within: 255, per: "Inc"))!.Message)
            .Contains("within must be 1 to 254");

        static Spec<int> SixtyFive()
        {
            var spec = Counter();
            for (int i = 0; i < 65; i++) spec.Precedes($"P{i}", "quote", (_, a) => a == 1, (_, a) => a > 1);
            return spec;
        }
        await Assert.That(Assert.Throws<CsCheckException>(() => SixtyFive())!.Message).Contains("64 Precedes");

        await Assert.That(Assert.Throws<CsCheckException>(() => Mixed(0, 17))!.Message)
            .Contains("limit of 16 Response and AtMost");
        await Assert.That(Assert.Throws<CsCheckException>(
            () => Counter().AtMost("A", "q", 255, (_, _) => true))!.Message).Contains("times must be 0 to 254");
    }

    /// <summary>The reason coverage is counted per (action, argument) case and not per action. Set(2) is never enabled,
    /// and the other two cases keep Set busy - so counting per action would report a healthy total and NeverFired could
    /// not see the dead case at all. This is the argument-level half of the vacuity story, and it is exactly the shape
    /// of the hole the per-action count left in the FIX example's twenty inbound cases.</summary>
    [Test]
    public async Task NeverFired_Detects_A_Dead_Argument_Case()
    {
        var report = Spec.From(0)
            .Action("Set", [1, 2, 3], (_, v) => v != 2, (_, v) => v)
            .Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(string.Join(",", report.NeverFired)).IsEqualTo("Set(2)");
        await Assert.That(report.ToString()).Contains("| Set(2)      |       NEVER |");
        // The other two cases are busy, which is what would have hidden it.
        await Assert.That(report.ToString()).Contains("| Set(1)      |");
    }

    /// <summary>The Response half of the shared slot pool. The AtMost test below covers the ninth slot's word selection
    /// for a count; this covers it for a deadline, which is the more involved path - it decrements, can be cancelled and
    /// is measured per action. Same model and bound as Response_Obligation_Survives_A_Cycle, so the four step
    /// counterexample is directly comparable with the Response sitting in slot zero there.</summary>
    [Test]
    public async Task A_Response_Past_The_Eighth_Slot_Still_Expires()
    {
        var spec = Spec.From(0).Action("Tick", i => (i + 1) % 2);
        for (int i = 0; i < 8; i++) spec.AtMost($"PAD{i}", "cannot occur", 3, (_, _) => false);
        spec.Response("NINTH", "entering one must be followed by settling",
            trigger: (b, a) => b == 0 && a == 1, response: (_, _) => false, within: 3, per: "Tick");
        spec.Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NINTH");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(4);
    }

    /// <summary>Mermaid writes state labels into quoted strings, so a printer that emits quotes, pipes or newlines needs
    /// escaping before the graph can render.</summary>
    [Test]
    public async Task Mermaid_Escapes_Label_Syntax()
    {
        var mermaid = Spec.From(0)
            .Action("Step", i => i < 1, i => i + 1)
            .Print(i => "say \"hi\" | " + i + "\nnext")
            .Mermaid();
        TUnitX.WriteLine(mermaid);
        await Assert.That(mermaid).Contains("n0[\"say &quot;hi&quot; &#124; 0<br/>next\"]");
    }

    /// <summary>The emitted Mermaid is text people copy into Markdown, so its stable shape is part of the API.</summary>
    [Test]
    public async Task Mermaid_Text_Output_Is_Stable()
    {
        var mermaid = Spec.From(0)
            .Action("Step", i => i < 1, i => i + 1)
            .Terminal(i => i == 1)
            .Mermaid();
        TUnitX.WriteLine(mermaid);
        await Assert.That(mermaid).IsEqualTo(
            """
            flowchart LR
              classDef initial stroke:#495057,stroke-width:2px,stroke-dasharray: 3 3;
              classDef terminal stroke:#0b7285,stroke-width:4px;
              classDef deadlock stroke:#c92a2a,stroke-width:4px;
              classDef truncated stroke:#f08c00,stroke-width:4px,stroke-dasharray: 5 5;
              classDef note stroke:#868e96,stroke-dasharray: 3 3;
              start((start));
              start --> n0;
              class start initial;
              n0 -->|"Step"| n1;
              n0["0"];
              n1["1"];
              class n1 terminal;
              info["states: 2<br/>edges: 1<br/>terminal: 1<br/>deadlock: 0"];
              class info note;
              subgraph legend["Legend"]
                legendTerminal["terminal"];
                legendDeadlock["deadlock"];
                legendTruncated["truncated"];
              end
              class legendTerminal terminal;
              class legendDeadlock deadlock;
              class legendTruncated truncated;
            """ + "\n");
    }

    /// <summary>A picture stops being useful long before a proof does, so Mermaid gives up at its own much smaller limit.
    /// Every other Mermaid test draws a model well inside it, leaving the truncation path unrun - and it was wrong: the walk
    /// stopped as soon as the cap was reached, so the last state discovered kept the edge that found it but never got a
    /// label of its own. Every state that is pointed at must be declared.</summary>
    [Test]
    public async Task Mermaid_Truncates_At_MaxStates()
    {
        var mermaid = Spec.From(0).Action("Step", i => i < 10, i => i + 1).Mermaid(maxStates: 4);
        TUnitX.WriteLine(mermaid);
        await Assert.That(mermaid).Contains("truncatedNote[\"gave up at 4 states\"]");
        // Four labels for four states, and three edges between them. Every n referenced by an edge is declared.
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
        await Assert.That(Regex.Count(mermaid, @"n\d+\[""\d")).IsEqualTo(4);
        await Assert.That(Regex.Count(mermaid, " -->\\|")).IsEqualTo(3);
        foreach (var to in Regex.Matches(mermaid, @" -->\|""[^""]+""\| (n\d+)"))
            await Assert.That(mermaid).Contains(((Match)to).Groups[1].Value + "[\"");
        // The state whose successor was dropped is dashed, so it cannot be read as an intended end or a dead one.
        await Assert.That(Regex.Count(mermaid, @"class n\d+ truncated")).IsEqualTo(1);
        await Assert.That(Regex.Count(mermaid, @"class n\d+ deadlock")).IsEqualTo(0);
        await Assert.That(mermaid).EndsWith("\n");
    }

    /// <summary>The note says an edge was dropped, so a model that happens to be exactly the size of the cap and was
    /// drawn in full must not claim it gave up.</summary>
    [Test]
    public async Task Mermaid_Exactly_At_MaxStates_Is_Not_Truncated()
    {
        var mermaid = Spec.From(0).Action("Step", i => i < 3, i => i + 1).Mermaid(maxStates: 4);
        TUnitX.WriteLine(mermaid);
        await Assert.That(mermaid).DoesNotContain("gave up");
        await Assert.That(Regex.Count(mermaid, @"class n\d+ truncated")).IsEqualTo(0);
        await Assert.That(Regex.Count(mermaid, @"n\d+\[""\d")).IsEqualTo(4);
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
    }

    /// <summary>Slots eight and above sit in the second counter word, reached through a ref conditional, so only a spec
    /// that spends past the eighth exercises that path at all. The requirement under test here is the ninth, and the
    /// eight before it are padding that can never occur - if the word selection were wrong it would either read a
    /// padding slot's count or corrupt one.</summary>
    [Test]
    public async Task Requirements_Past_The_Eighth_Slot_Still_Count()
    {
        static Spec<int> Ninth(int bound)
        {
            var spec = Spec.From(0).Action("Inc", i => i < 6, i => i + 1);
            for (int i = 0; i < 8; i++) spec.AtMost($"PAD{i}", "cannot occur", 3, (_, _) => false);
            return spec.AtMost("NINTH", "at most bound increments", bound, (b, a) => a > b);
        }
        var ok = Ninth(6).Exhaustive();
        await Assert.That(ok.Closed).IsTrue();

        Ninth(3).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NINTH");
        await Assert.That(violation.Detail).Contains("more than 3 times");
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(4);
    }

    /// <summary>Every other Sample here expects to pass, so the failure path of the random engine - detect, then hand
    /// the trace to CsCheck to shrink - had no test at all. Five steps is the shortest way to reach five, so a sixth
    /// step in the reported trace would mean the shrink did not finish.</summary>
    [Test]
    public async Task Sample_Finds_And_Shrinks_A_Violation()
    {
        var spec = Spec.From(0)
            .Action("Inc", i => i < 20, i => i + 1)
            .Never("NO-FIVE", "the counter never reaches five", (_, a) => a == 5);
        var message = Assert.Throws<CsCheckException>(() => spec.Sample(maxSteps: 30, iter: 10_000))!.Message;
        TUnitX.WriteLine(message);
        await Assert.That(message).Contains("NO-FIVE");
        await Assert.That(message).Contains(" 5 Inc");
        await Assert.That(message).DoesNotContain(" 6 Inc");
    }

    /// <summary>The signal Faults exists for. Elsewhere the tables assert nothing escaped; this asserts that when
    /// something does, the run fails rather than printing a NOTHING row into output no one reads.</summary>
    [Test]
    public async Task Faults_Throws_When_A_Defect_Escapes()
    {
        var lines = new List<string>();
        var spec = Spec.From(0)
            .Action("Inc", i => i < 5, i => i + 1)
            .Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Fault("counter advances twice", (_, _) => true, (_, a) => a + 1);
        var message = Assert.Throws<CsCheckException>(() => spec.Faults(lines.Add))!.Message;
        TUnitX.WriteLine(string.Join('\n', lines));
        await Assert.That(message).Contains("counter advances twice");
        await Assert.That(string.Join('\n', lines)).Contains("NOTHING");
    }

    /// <summary>The Caught by column is what the examples assert on, and a fault renamed without its assertion being
    /// updated would otherwise read as uncaught - the same green-for-the-wrong-reason failure the column exists to
    /// catch. So an unknown name throws rather than returning null.</summary>
    [Test]
    public async Task CaughtBy_An_Unknown_Fault_Name_Is_Rejected()
    {
        // Action stops at 4, so the unfaulted spec never reaches 5. The fault jumps to 5 on any step.
        var report = Spec.From(0).Action("Inc", i => i < 4, i => i + 1)
            .Invariant("NON-NEGATIVE", "the counter never goes negative", i => i >= 0)
            .Never("NO-FIVE", "the counter never reaches five", (_, a) => a == 5)
            .Fault("counter jumps to five", (_, _) => true, (_, _) => 5)
            .Faults(throwOnUncaught: false);
        await Assert.That(report.CaughtBy("counter jumps to five")).IsEqualTo("NO-FIVE");
        await Assert.That(Assert.Throws<CsCheckException>(() => report.CaughtBy("counter jumps to six"))!.Message)
            .Contains("counter jumps to six");
    }

    /// <summary>maxDepth stops the walk short, so the space has not closed and nothing may be concluded from it.</summary>
    [Test]
    public async Task MaxDepth_Stops_Short_Of_Closing()
    {
        var report = Spec.From(0).Action("Inc", i => i < 10, i => i + 1).Exhaustive(maxDepth: 3);
        await Assert.That(report.Closed).IsFalse();
        await Assert.That(report.Depth).IsEqualTo(3);
        await Assert.That(report.Note).Contains("maxDepth 3");
        var closed = Spec.From(0).Action("Inc", i => i < 10, i => i + 1).Exhaustive();
        await Assert.That(closed.Closed).IsTrue();
        await Assert.That(closed.States).IsEqualTo(11);
    }

    /// <summary>Pruning happens during the sequential insert, so like everything else about Exhaustive the result does
    /// not depend on how many threads expanded it.</summary>
    [Test]
    public async Task Boundary_Is_Independent_Of_Thread_Count()
    {
        static Spec<(int, int)> Grid() => Spec.From((0, 0))
            .Action("X", t => (t.Item1 + 1, t.Item2))
            .Action("Y", t => (t.Item1, t.Item2 + 1))
            .Boundary(t => t.Item1 + t.Item2 <= 12);
        var one = Grid().Exhaustive(threads: 1);
        var many = Grid().Exhaustive(threads: Environment.ProcessorCount);
        await Assert.That(many.States).IsEqualTo(one.States);
        await Assert.That(many.Pruned).IsEqualTo(one.Pruned);
        await Assert.That(many.ToString()).IsEqualTo(one.ToString());
        // Distinct states outside the boundary, not transitions into them: the 14 states summing to 13, each reachable
        // two ways. Pinned because it is a number a caller can read and the two differ by nearly a factor of two.
        await Assert.That(one.States).IsEqualTo(91);
        await Assert.That(one.Pruned).IsEqualTo(14);
    }

    /// <summary>Every transition fires exactly one (action, argument) case, so the coverage table's numbers have to sum
    /// to the transition count. The thread agreement tests compare one thread against many, which catches the two
    /// disagreeing but not a miscount present in both; this is the absolute check. It is the one that would catch the
    /// parallel path folding its per node fired arrays in wrongly, since that path accumulates into a buffer and adds it
    /// up afterwards rather than incrementing as it goes.
    /// <para>Only on a run that closed. A violation stops the inserting while the rest of that node's edges are still
    /// evaluated for coverage, and giving up at maxStates does the same, so both leave Fired ahead of Transitions by</para>
    /// design - which is itself worth stating, because it looks like a bug until you know why.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(4)]
    public async Task Action_Coverage_Sums_To_The_Transition_Count(int threads)
    {
        foreach (var report in new[]
        {
            FencingSpec.Create(FencingSpec.Fence.Every).Exhaustive(threads: threads),
            AlternatingBitSpec.Create().Exhaustive(threads: threads),
            RefreshCacheSpec.Create().Exhaustive(threads: threads),
        })
        {
            await Assert.That(report.Closed).IsTrue();
            var fired = 0L;
            foreach (var f in report.ActionFired) fired += f;
            await Assert.That(fired).IsEqualTo(report.Transitions);
        }
    }

    /// <summary>The same arithmetic for the random walk, where it is checking something the exhaustive engine does not
    /// have: a thread static tally per walk, flushed into the shared counters with interlocked adds. <c>Sample</c> runs
    /// on every core by default, so that code is always threaded and nothing else measures whether it loses or double
    /// counts. Every step of every trace fires exactly one case, so the sum has to be the step count, and the walk count
    /// has to be the iterations asked for.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(-1)]
    public async Task Sample_Coverage_Sums_To_The_Steps_Walked(int threads)
    {
        var report = FixEngineSpec.Create().Sample(iter: 500, threads: threads);
        await Assert.That(report.TracesWalked).IsEqualTo(500);
        var fired = 0L;
        foreach (var f in report.ActionFired) fired += f;
        await Assert.That(fired).IsEqualTo(report.StepsWalked);
        // And the steps are the traces' own lengths, so neither counter can drift from the other.
        await Assert.That(report.StepsWalked).IsGreaterThan(500);
    }
}
