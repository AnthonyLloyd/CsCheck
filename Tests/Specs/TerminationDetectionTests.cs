namespace Tests.Specs;

using System.Diagnostics;
using CsCheck;

/// <summary>Verified against TLC 1.7.4 on EWD998Small.cfg (N=3, the original's own StateConstraint): TLC gives
/// <b>1,520,618 distinct states and 11,238,019 generated</b>. Our 1,520,691 = TLC's 1,520,618 plus the 73 setup
/// scaffold states (the 3-step setup prefix before any protocol initial state). The "1.3m" note embedded in
/// EWD998.tla was wrong; running TLC today on the same spec gives 1.52M.</summary>
public class TerminationDetectionTests
{
    /// <summary>Safra's algorithm never announces termination while work is outstanding, and Safra's inductive invariant
    /// holds, which is the argument for why. Both over the region the original's own StateConstraint picks out.
    /// <para>Verified against TLC 1.7.4: 1,520,618 distinct states, 11,238,019 generated. Our count differs by 73 —
    /// the scaffold states from the three setup actions that choose among the 192 protocol initial configurations
    /// that TLA+'s free-ranging <c>Init</c> gets for nothing.</para></summary>
    [Test]
    public async Task Termination_Is_Never_Detected_Early()
    {
        var report = TerminationDetectionSpec.Create().Exhaustive(TUnitX.WriteLine, maxStates: 4_000_000);
        TUnitX.WriteLine($"\n{report.States:#,0} states  (TLC 1.7.4: 1,520,618 + 73 scaffold = 1,520,691)");
        // TLC's "states generated" (11.2M) includes out-of-boundary successors; our Transitions only counts edges
        // within the boundary (10.5M). The counts are semantically different, not a discrepancy.
        TUnitX.WriteLine($"{report.Transitions:#,0} within-boundary transitions  depth {report.Depth}");
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        await Assert.That(report.States).IsEqualTo(1_520_691);
        await Assert.That(report.Transitions).IsGreaterThan(10_000_000);
        await Assert.That(report.Transitions).IsLessThan(11_000_000);
        await Assert.That(report.Depth).IsEqualTo(61);
    }

    /// <summary>Copilot's PR review suggested missing lower bounds on counters might explain the apparent discrepancy
    /// with TLC's published "1.3m". Running TLC 1.7.4 directly resolved it: TLC gives 1,520,618, not 1.3M — the
    /// comment in EWD998.tla was simply wrong. Adding lower bounds gives fewer states and a worse depth; they are not
    /// needed and the upper-only bounds are the faithful replication of the written StateConstraint.
    /// <para>The initial colouring question is still worth answering: TLA+'s Init allows any combination of colours (192
    /// initial states), while our SetupColour only picks one of them for the anyInitialColour=false case. The count
    /// barely moves (1,520,691 vs 1,514,331), confirming it is not the meaningful dimension.</para></summary>
    [Test]
    public async Task The_Boundary_Is_Faithful_To_The_Original()
    {
        var counts = new List<int>();
        foreach (var any in new[] { true, false })
        {
            var report = TerminationDetectionSpec.Create(anyInitialColour: any).Exhaustive(maxStates: 4_000_000);
            TUnitX.WriteLine($"initial colours {(any ? "any  " : "white")}  {report.States,9:#,0} states "
                + $"{report.Transitions,12:#,0} transitions  depth {report.Depth}");
            await Assert.That(report.Closed).IsTrue();
            await Assert.That(report.Depth).IsEqualTo(61);
            counts.Add(report.States);
        }
        // Under one percent apart, so 192 initial states against 24 is not what the gap against 1.3 million is.
        await Assert.That((counts[0] - counts[1]) * 100.0 / counts[0]).IsLessThan(1.0);
    }

    /// <summary>The safety property is not vacuous, and the ways it could have been are each ruled out separately: the
    /// system can actually terminate, the algorithm can actually notice, a message can be in flight and a node can be
    /// blackened. A run where the token never came home would satisfy the invariant and prove nothing.</summary>
    [Test]
    public async Task The_Interesting_States_Are_All_Reached()
    {
        var report = TerminationDetectionSpec.Create(counterMax: 1, pendingMax: 1, tokenMax: 2)
            .Exhaustive(TUnitX.WriteLine, maxStates: 4_000_000);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
        var table = report.ToString();
        foreach (var id in new[] { "CAN-TERMINATE", "CAN-DETECT", "CAN-FLY", "CAN-BLACKEN" })
            await Assert.That(table).Contains(id);
    }

    /// <summary>How the space grows with the bound, and evidence the bound is not what makes the algorithm look correct.
    /// The original's own constraint is the largest row. Skipped in normal CI because the final row (1.5M states)
    /// takes several seconds; run explicitly to see the scaling table.</summary>
    [Test, Skip("Long-running; run explicitly")]
    public async Task Growth_With_The_Bound()
    {
        // (2,2,4) and (2,2,9) produce identical state counts — the token accumulator bound only matters when it
        // can exceed the sum of counter values, which it cannot at pendingMax=2.
        foreach (var (c, p, q) in new[] { (1, 1, 2), (2, 2, 9), (3, 3, 9) })
        {
            var sw = Stopwatch.StartNew();
            var report = TerminationDetectionSpec.Create(c, p, q).Exhaustive(maxStates: 4_000_000);
            sw.Stop();
            TUnitX.WriteLine($"counter<={c} pending<={p} q<={q}  {report.States,9:#,0} states "
                + $"{report.Transitions,11:#,0} transitions  depth {report.Depth,3}  {report.Pruned,9:#,0} outside  "
                + $"{sw.Elapsed.TotalSeconds,5:0.0}s");
            await Assert.That(report.Closed).IsTrue();
        }
    }

    /// <summary>Breaking the algorithm has to break the property, or the property is not what makes it work. Rule 3 says
    /// receiving a message blackens the receiver, which is what stops a token that passed a node before it received
    /// anything from coming home white. Dropping that rule is the classic way to get EWD998 wrong.</summary>
    [Test]
    public async Task Dropping_Rule_3_Detects_Termination_Early()
    {
        var spec = TerminationDetectionSpec.Create()
            // A receipt that does not blacken. Faults perturb the state after the step, so this puts the colour back.
            .Fault("NoBlackenOnReceive",
                (b, a) => a.Black != b.Black && a.InFlight < b.InFlight,
                (b, a) => a with { Black = b.Black });
        var report = spec.Faults(TUnitX.WriteLine, maxStates: 4_000_000, throwOnUncaught: false);
        await Assert.That(report.Uncaught).IsEmpty();
        // Safra's inductive invariant (not just the safety property) is what detects this. If a refactoring
        // accidentally split the invariant and lost the structural argument, the safety property might still hold
        // superficially while this test would catch the loss.
        await Assert.That(report.CaughtBy("NoBlackenOnReceive")).IsEqualTo("SAFRA-INV");
    }
}
