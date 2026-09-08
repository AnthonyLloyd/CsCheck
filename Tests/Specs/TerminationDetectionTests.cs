namespace Tests.Specs;

using System.Diagnostics;
using CsCheck;

/// <summary>The original publishes its own TLC numbers for this configuration, so this is the one example whose size can
/// be checked rather than only reported.</summary>
public class TerminationDetectionTests
{
    /// <summary>Safra's algorithm never announces termination while work is outstanding, and Safra's inductive invariant
    /// holds, which is the argument for why. Both over the region the original's own StateConstraint picks out.
    ///
    /// The published TLC run for a ring of three reports 1.3 million distinct states, 10.1 million generated and a
    /// diameter of 60. Transitions and depth land on those; the distinct count comes out higher, and the next test rules
    /// out the obvious reason without finding the real one.</summary>
    [Test]
    public async Task Termination_Is_Never_Detected_Early()
    {
        var sw = Stopwatch.StartNew();
        var report = TerminationDetectionSpec.Create().Exhaustive(TUnitX.WriteLine, maxStates: 4_000_000);
        sw.Stop();
        TUnitX.WriteLine($"\n{report.States:#,0} states, {report.Transitions:#,0} transitions, depth {report.Depth}, "
            + $"{sw.Elapsed.TotalSeconds:0.0}s");
        TUnitX.WriteLine("published for N=3: 1.3m distinct states, diameter 60");
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        // Transitions and depth land on the published figures; distinct states are higher, and the next test says why.
        await Assert.That(report.Transitions).IsGreaterThan(10_000_000);
        await Assert.That(report.Transitions).IsLessThan(11_000_000);
        // TLC's diameter of 60 counts the states along the longest shortest path, so 59 steps between them; the
        // three setup actions in front of the protocol add two more than TLA+'s free choice of initial state.
        await Assert.That(report.Depth).IsEqualTo(61);
    }

    /// <summary>The distinct state count comes out about seventeen percent above the published 1.3 million while the
    /// transitions and the diameter land on it, and this rules out the obvious explanation. The current original starts
    /// from any colouring (<c>color \in [Node -&gt; Color]</c>), which is 192 initial states against 24 for an all-white
    /// start - but narrowing it moves the count by less than half a percent, so the initial set is not where the
    /// difference is. The published figures are from a January 2021 run of a module that has been revised since, which
    /// leaves the residual unexplained rather than explained; it is recorded here rather than papered over.</summary>
    [Test]
    public async Task The_Initial_Colouring_Does_Not_Account_For_The_Difference()
    {
        var counts = new List<int>();
        foreach (var any in new[] { true, false })
        {
            var report = TerminationDetectionSpec.Create(anyInitialColour: any).Exhaustive(maxStates: 4_000_000);
            TUnitX.WriteLine($"initial colours {(any ? "any  " : "white")}  {report.States,9:#,0} states "
                + $"{report.Transitions,11:#,0} transitions  depth {report.Depth}");
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
        foreach (var id in new[] { "CAN-TERMINATE", "CAN-DETECT", "CAN-FLY", "CAN-BLACKEN" })
            await Assert.That(report.ToString()).Contains(id);
    }

    /// <summary>How the space grows with the bound, and evidence the bound is not what makes the algorithm look correct.
    /// The original's own constraint is the largest row.</summary>
    [Test]
    public async Task Growth_With_The_Bound()
    {
        foreach (var (c, p, q) in new[] { (1, 1, 2), (2, 2, 4), (2, 2, 9), (3, 3, 9) })
        {
            var sw = Stopwatch.StartNew();
            var report = TerminationDetectionSpec.Create(c, p, q).Exhaustive(maxStates: 4_000_000);
            sw.Stop();
            TUnitX.WriteLine($"counter<={c} pending<={p} q<={q}  {report.States,9:#,0} states "
                + $"{report.Transitions,10:#,0} transitions  depth {report.Depth,3}  {report.Pruned,7:#,0} outside  "
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
        TUnitX.WriteLine($"caught by {report.CaughtBy("NoBlackenOnReceive")}");
    }
}
