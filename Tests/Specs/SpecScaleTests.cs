namespace Tests.Specs;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CsCheck;

/// <summary>How big a model can be before Exhaustive stops being practical, measured rather than guessed. The answer
/// decides whether an abstraction is worth tightening, and it is the number the "give up at maxStates" note is really
/// about. It also says why the engine is single threaded: at these rates the binding constraint is memory, and
/// parallelism does not help memory.</summary>
public class SpecScaleTests
{
    readonly record struct Cube(int A, int B, int C);

    /// <summary>A model whose size is a knob: (n+1) cubed states, three transitions out of each. Deliberately a
    /// realistic requirement load rather than none, so the per transition cost is not flattered.</summary>
    static Spec<Cube> Cubes(int n)
        => Spec.From(new Cube(0, 0, 0))
        .Action("A", c => c.A < n, c => c with { A = c.A + 1 })
        .Action("B", c => c.B < n, c => c with { B = c.B + 1 })
        .Action("C", c => c.C < n, c => c with { C = c.C + 1 })
        .Terminal(c => c.A == n && c.B == n && c.C == n)
        .Invariant("BOUNDED", "each coordinate stays in range", c => c.A <= n && c.B <= n && c.C <= n)
        .Reachable("CORNER", "the far corner is reachable", c => c.A == n && c.B == n && c.C == n)
        .Never("MONOTONIC", "no coordinate ever decreases", (b, a) => a.A < b.A || a.B < b.B || a.C < b.C)
        .Rule("ONE-AT-A-TIME", "exactly one coordinate moves per step",
            (b, a) => a.A + a.B + a.C == b.A + b.B + b.C + 1);

    [Test]
    public async Task Exhaustive_Scale()
    {
        foreach (var n in new[] { 20, 40, 60 })
        {
            // Built before the measurement, and reused, so the spec's own allocation is not counted in bytes/state.
            var spec = Cubes(n);
            spec.Exhaustive(maxStates: 5_000_000);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var report = spec.Exhaustive(maxStates: 5_000_000);
            sw.Stop();
            var bytes = (GC.GetTotalMemory(false) - before) / (double)report.States;
            var rate = report.Transitions / sw.Elapsed.TotalSeconds;
            TUnitX.WriteLine($"n={n,2} {report.States,9:#,0} states {report.Transitions,10:#,0} transitions "
                + $"{sw.Elapsed.TotalMilliseconds,7:0.0}ms {rate / 1e6,5:0.00}M/s {bytes,5:0} bytes/state");
            await Assert.That(report.Closed).IsTrue();
        }
    }

    /// <summary>The property that makes a parallel proof engine acceptable: the answer must not depend on how many
    /// threads ran it. Expansion is parallel but insertion is sequential in source order, so state and transition
    /// counts, depth, terminal and deadlock counts, and every coverage number come out the same.</summary>
    [Test]
    public async Task Exhaustive_Is_Independent_Of_Thread_Count()
    {
        foreach (var n in new[] { 8, 25 })
        {
            // One spec object run twice, which is the stronger claim: not two equal specs agreeing, but the same one.
            var spec = Cubes(n);
            var one = spec.Exhaustive(threads: 1);
            var many = spec.Exhaustive(threads: Environment.ProcessorCount);
            await Assert.That(many.States).IsEqualTo(one.States);
            await Assert.That(many.Transitions).IsEqualTo(one.Transitions);
            // Not in ToString, so it needs saying separately - and it decides which "did not close" note a user gets.
            await Assert.That(many.Revisits).IsEqualTo(one.Revisits);
            await Assert.That(many.Depth).IsEqualTo(one.Depth);
            await Assert.That(many.TerminalStates).IsEqualTo(one.TerminalStates);
            await Assert.That(many.DeadlockStates).IsEqualTo(one.DeadlockStates);
            await Assert.That(many.ToString()).IsEqualTo(one.ToString());
        }
    }

    /// <summary>And the same for a counterexample. Several violations can sit at the same depth, so the one reported
    /// has to be chosen by source order rather than by whichever thread got there first.</summary>
    [Test]
    public async Task Counterexample_Is_Independent_Of_Thread_Count()
    {
        var spec = Cubes(6).Never("NO-DIAGONAL", "the diagonal is never reached",
            (_, a) => a.A == a.B && a.B == a.C && a.A > 0);
        // The whole report, not just the counterexample. A violation stops the walk mid node, and the two paths reach
        // that point differently - one fused, one having already expanded the level - so coverage is where they would
        // drift. Cubes would not catch it: its violating action is the last one declared, so both paths happen to
        // evaluate the same edges. Early() puts the violation on the first of two actions, where they did differ.
        static Spec<int> Early() => Spec.From(0)
            .Action("Bad", i => i + 100)
            .Action("Good", i => i + 1)
            .Never("NO-BIG", "the counter never reaches a hundred", (_, a) => a >= 100);
        var e1 = Early().Exhaustive(out _, threads: 1);
        var eN = Early().Exhaustive(out _, threads: Environment.ProcessorCount);
        await Assert.That(eN.ToString()).IsEqualTo(e1.ToString());
        await Assert.That(e1.NeverFired).IsEmpty();

        spec.Exhaustive(out var one, threads: 1);
        var manyReport = spec.Exhaustive(out var many, threads: Environment.ProcessorCount);
        await Assert.That(manyReport.ToString()).IsEqualTo(spec.Exhaustive(out _, threads: 1).ToString());
        await Assert.That(one).IsNotNull();
        await Assert.That(many!.Id).IsEqualTo(one!.Id);
        await Assert.That(many.Trace.Steps.Length).IsEqualTo(one.Trace.Steps.Length);
        await Assert.That(many.ToString()).IsEqualTo(one.ToString());
        TUnitX.WriteLine(many.ToString(c => c.ToString()));
    }

    /// <summary>The thread checks above use Cubes, which has only Invariant, Reachable, Rule and Never - none of the
    /// forms that carry state between steps. So they never exercise the part of the parallel path most likely to be
    /// wrong: outstanding deadlines and history bits threaded through a buffered chunk, and per requirement coverage
    /// counts accumulated at an offset into a flat array. The worked examples between them use Response, Precedes and
    /// NeverAfter, and comparing the whole report catches a misattributed count as well as a wrong state.</summary>
    [Test]
    public async Task Deadlines_And_History_Are_Independent_Of_Thread_Count()
    {
        // The row names assert the comparison actually covers a state carrying form, so this cannot pass by comparing
        // two reports that happen to contain nothing interesting.
        await Same(FixEngineSpec.Create(), "LOGOUT-COMPLETES", "NO-APP-BEFORE-LOGON");
        await Same(RefreshCacheSpec.Create(), "NEVER-MISS-TWICE");
        await Same(FencingSpec.Create(FencingSpec.Fence.Every), "SUPERSEDED-TOKEN-REFUSED[1]");

        static async Task Same<S>(Spec<S> spec, params string[] rows)
        {
            var one = spec.Exhaustive(threads: 1);
            var many = spec.Exhaustive(threads: Environment.ProcessorCount);
            await Assert.That(one.Closed).IsTrue();
            foreach (var row in rows) await Assert.That(one.ToString()).Contains(row);
            await Assert.That(many.ToString()).IsEqualTo(one.ToString());
        }
    }

    /// <summary>The deadlock path is the one part of the report built from a single remembered node rather than from a
    /// counter, so it is the part that would quietly depend on which thread reached that node first. Nothing else
    /// exercises it: three of the worked examples have no deadlock at all. BlockingQueue has many.</summary>
    [Test]
    public async Task The_Deadlock_Path_Is_Independent_Of_Thread_Count()
    {
        foreach (var (p, c, b) in new[] { (2, 2, 1), (3, 3, 1), (3, 3, 2) })
        {
            var spec = BlockingQueueSpec.Create(BlockingQueueSpec.Wake.Any, p, c, b);
            var one = spec.Exhaustive(threads: 1);
            var many = spec.Exhaustive(threads: Environment.ProcessorCount);
            await Assert.That(one.DeadlockStates).IsGreaterThan(0);
            await Assert.That(many.DeadlockTrace).IsEqualTo(one.DeadlockTrace);
            await Assert.That(many.ToString()).IsEqualTo(one.ToString());
        }
    }

    /// <summary>Two saturating fields, so any specification built from them is finite however the actions are wired.
    /// The cap is what sets the size of the space, and it is high enough that a level of the frontier is worth handing
    /// to more than one thread - the point of the test below is lost on a model whose every level holds one node.</summary>
    readonly record struct Tiny(int A, int B)
    {
        public override string ToString() => $"({A},{B})";
    }

    const int Cap = 24;

    readonly record struct ActSpec(int DA, int DB, int Guard);
    readonly record struct ReqSpec(int Kind, int K, int N);

    /// <summary>Random small specifications: a few guarded actions over a two value argument domain, and a mixture of
    /// requirement forms so the deadline, history and count words are all in play. Guard and trigger thresholds stay
    /// small while the cap is large, so requirements fire early rather than sitting unreachable.</summary>
    static Gen<Spec<Tiny>> GenSpec =>
        from acts in Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[0, Cap],
                                (dA, dB, g) => new ActSpec(dA, dB, g)).Array[1, 3]
        from reqs in Gen.Select(Gen.Int[0, 6], Gen.Int[0, 3], Gen.Int[1, 3],
                                (kind, k, n) => new ReqSpec(kind, k, n)).Array[0, 4]
        select Build(acts, reqs);

    static Spec<Tiny> Build(ActSpec[] acts, ReqSpec[] reqs)
    {
        var spec = Spec.From(new Tiny(0, 0));
        for (int i = 0; i < acts.Length; i++)
        {
            var act = acts[i];
            spec.Action($"Act{i}", [0, 1], (t, arg) => t.A + t.B <= act.Guard + arg,
                (t, arg) => new Tiny(Math.Min(t.A + act.DA + arg, Cap), Math.Min(t.B + act.DB, Cap)));
        }
        for (int i = 0; i < reqs.Length; i++)
        {
            var r = reqs[i];
            switch (r.Kind)
            {
                case 0: spec.Invariant($"I{i}", "q", t => t.A <= Cap && t.B <= Cap); break;
                case 1: spec.Never($"N{i}", "q", (_, a) => a.A == r.K && a.B == r.K); break;
                case 2: spec.Rule($"R{i}", "q", (b, a) => a.A >= b.A); break;
                case 3: spec.AtMost($"M{i}", "q", r.N, (b, a) => a.A > b.A); break;
                case 4: spec.Response($"P{i}", "q", (_, a) => a.A == r.K, (_, a) => a.B > r.K, within: r.N); break;
                case 5: spec.NeverAfter($"Z{i}", "q", (_, a) => a.A >= r.K, (b, a) => a.B < b.B); break;
                default: spec.Precedes($"C{i}", "q", (_, a) => a.B > r.K, (_, a) => a.A > r.K); break;
            }
        }
        return spec;
    }

    /// <summary>Only the sound direction: a walk that finds nothing proves nothing.</summary>
    [Test]
    public async Task A_Closed_Proof_Cannot_Be_Refuted_By_Sampling()
    {
        int seen = 0, proved = 0;
        GenSpec.Sample(spec =>
        {
            seen++;
            if (!spec.Exhaustive(out var violation, maxStates: 5_000).Closed || violation is not null) return true;
            proved++;
            try { spec.Sample(maxSteps: 20, iter: 200); return true; }
            catch (CsCheckException) { return false; }
        }, iter: 200, threads: 1);
        // A share of those drawn, not of the iterations asked for, since CsCheck_Time decides how many that is.
        TUnitX.WriteLine($"{proved} of {seen} specs closed clean and were then sampled");
        await Assert.That(proved * 10).IsGreaterThan(seen);
    }

    /// <summary>Conform drives real code down a trace, so a gap would step it from a state it was never in.</summary>
    [Test]
    public void Every_Trace_Is_Contiguous()
    {
        GenSpec.Sample(spec =>
        {
            var contiguous = true;
            // About half of these violate something; the traces walked before that are what is under test.
            try
            {
                spec.Conform(() => new Tiny[1] { new(0, 0) }, (sut, t) =>
                {
                    contiguous &= t.Before.Equals(sut[0]);
                    sut[0] = t.After;
                    return true;
                }, maxSteps: 20, iter: 100);
            }
            catch (CsCheckException) { }
            return contiguous;
        }, iter: 100, threads: 1);
    }

    /// <summary>Arithmetic the report owes itself.</summary>
    [Test]
    public void A_Closed_Report_Is_Internally_Consistent()
    {
        GenSpec.Sample(spec =>
        {
            var report = spec.Exhaustive(out _, maxStates: 5_000);
            if (!report.Closed) return true;
            var fired = 0L;
            foreach (var f in report.ActionFired) fired += f;
            return fired == report.Transitions
                && report.TerminalStates + report.DeadlockStates <= report.States
                && report.Depth < report.States
                && report.DeadlockTraces == 0
                && (report.DeadlockTrace is null) == (report.DeadlockStates == 0);
        }, iter: 200, threads: 1);
    }

    /// <summary>The thread agreement tests above pick their models by hand, so they only prove it for the shapes someone
    /// thought of - and this invariant has broken once already, on coverage counts for the node a violation was found
    /// in. Here Exhaustive is its own oracle over arbitrary specifications: one thread is the reference answer and many
    /// threads must reproduce it exactly, which covers the state and transition counts, every per requirement and per
    /// argument coverage number, the pruned and revisit counts, the deadlock path and any counterexample, since all of
    /// them are in the report. Over five hundred draws the generator was measured to give a median of seventeen states
    /// and a maximum of three hundred and ninety four, to violate something in forty five per cent of them and to leave
    /// a deadlock in fifty five, so both the proved and the refuted paths are compared and neither is incidental.</summary>
    [Test]
    public void Exhaustive_Is_Thread_Independent_For_Any_Spec()
    {
        GenSpec.Sample(spec =>
        {
            var one = spec.Exhaustive(out var v1, maxStates: 5_000, threads: 1);
            var many = spec.Exhaustive(out var vN, maxStates: 5_000, threads: 4);
            return string.Equals(one.ToString(), many.ToString(), StringComparison.Ordinal)
                && v1 is null == vN is null
                && (v1 is null || string.Equals(v1.ToString(), vN!.ToString(), StringComparison.Ordinal));
        }, iter: 500, threads: 1);
    }

    /// <summary>Whether parallel expansion is worth anything, and when. The answer depends entirely on how expensive
    /// the user delegates are relative to the sequential dictionary insert, so measure both ends.</summary>
    [Test]
    public void Parallel_Speedup()
    {
        foreach (var cost in new[] { 0, 20, 200 })
        {
            var spec = Spec.From(new Cube(0, 0, 0))
                .Action("A", c => c.A < 25, c => c with { A = c.A + 1 })
                .Action("B", c => c.B < 25, c => c with { B = c.B + 1 })
                .Action("C", c => c.C < 25, c => c with { C = c.C + 1 })
                .Invariant("WORK", "a stand in for a real requirement", _ => Spin(cost) >= 0);
            spec.Exhaustive(threads: 1);
            var one = Stopwatch.StartNew();
            var r = spec.Exhaustive(threads: 1);
            one.Stop();
            spec.Exhaustive(threads: Environment.ProcessorCount);
            var many = Stopwatch.StartNew();
            spec.Exhaustive(threads: Environment.ProcessorCount);
            many.Stop();
            TUnitX.WriteLine($"delegate cost {cost,3}: 1 thread {one.Elapsed.TotalMilliseconds,7:0.0}ms   "
                + $"{Environment.ProcessorCount} threads {many.Elapsed.TotalMilliseconds,7:0.0}ms   "
                + $"{one.Elapsed.TotalMilliseconds / many.Elapsed.TotalMilliseconds,4:0.00}x   ({r.Transitions:#,0} transitions)");
        }
    }

    static int Spin(int n)
    {
        var t = 0;
        for (int i = 0; i < n; i++) t += i % 7;
        return t;
    }

    /// <summary>The worked examples, for scale. All of them are three orders of magnitude below the point where any of
    /// this matters.</summary>
    [Test]
    public void Exhaustive_Worked_Examples()
    {
        Report("FIX", FixEngineSpec.Create());
        Report("Cache", RefreshCacheSpec.Create());
        Report("Fencing", FencingSpec.Create(FencingSpec.Fence.Every));
        Report("Queue", BlockingQueueSpec.Create(BlockingQueueSpec.Wake.Any, 3, 3, 2));
        Report("Disruptor", DisruptorSpec.Create(size: 3, sequences: 20));
        Report("EWD998", TerminationDetectionSpec.Create(counterMax: 2, pendingMax: 2, tokenMax: 4));
        Report("ABP", AlternatingBitSpec.Create());

        static void Report<S>(string name, Spec<S> spec)
        {
            spec.Exhaustive();
            var fresh = Stopwatch.StartNew();
            var report = spec.Exhaustive();
            fresh.Stop();
            TUnitX.WriteLine($"{name,-9} {report.States,9:#,0} states {report.Transitions,11:#,0} transitions "
                + $"{fresh.Elapsed.TotalMilliseconds,6:0.0}ms");
        }
    }
}
