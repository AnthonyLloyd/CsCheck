namespace Tests.Specs;

using CsCheck;

/// <summary>The only example whose state space does not close on its own, so the checks are about the bound as much as
/// about the protocol.</summary>
public class DisruptorTests
{
    /// <summary>The original's invariant, proved over the region the boundary picks out. The report says CLOSED within
    /// boundary rather than CLOSED, which is the honest claim: no data race is reachable without claiming more than the
    /// bound. Every coverage row has to have fired, and the wrap one especially - a ring buffer explored only up to its
    /// own size has not been explored as a ring.</summary>
    [Test]
    public async Task No_Data_Races_Within_The_Boundary()
    {
        var report = DisruptorSpec.Create(size: 3, sequences: 9).Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.Pruned).IsGreaterThan(0);
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        // Closure within a boundary is the weaker claim, and this is where that shows: the note says so.
        await Assert.That(report.ToString()).Contains("CLOSED within boundary");
    }

    /// <summary>What makes the bound believable. If the boundary were hiding a race, raising it would eventually find
    /// one; if it were hiding nothing, the answer stops changing while the space keeps growing. Neither is a proof for
    /// all sequences - only a normalisation of the counters would be that - but a flat answer over a space that grew
    /// twenty fold is the evidence available.</summary>
    [Test]
    public async Task Raising_The_Boundary_Does_Not_Change_The_Answer()
    {
        var previous = 0;
        foreach (var sequences in new[] { 4, 6, 9, 12, 16, 20 })
        {
            var report = DisruptorSpec.Create(size: 3, sequences: sequences).Exhaustive(maxStates: 2_000_000);
            TUnitX.WriteLine($"sequences <= {sequences,2}  {report.States,7:#,0} states {report.Transitions,8:#,0} "
                + $"transitions  {report.Pruned,5:#,0} outside  closed {report.Closed}");
            await Assert.That(report.Closed).IsTrue();
            await Assert.That(report.States).IsGreaterThan(previous);
            previous = report.States;
        }
    }

    /// <summary>The ring size is the other dimension, and the invariant has to hold for each. Size one is worth having:
    /// a single slot means every claim contends with every read, which is the case a gate off by one would break.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task No_Data_Races_For_Any_Ring_Size(int size)
    {
        var report = DisruptorSpec.Create(size, sequences: 4 * size).Exhaustive(maxStates: 2_000_000, writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>Why the design works, stated as its own requirement rather than assumed: a producer never gets more than
    /// one cycle ahead of the slowest consumer. That is the gate's direct consequence and it is what makes the slot a
    /// claim maps to free. Added here rather than in the specification because it is a theorem about the design, not one
    /// of the original's requirements, and because a loosened gate would then be caught by this before the race it
    /// causes - which would prove the window and say nothing about data races.</summary>
    [Test]
    public async Task Producers_Stay_Within_One_Cycle_Of_The_Slowest_Consumer()
    {
        var report = DisruptorSpec.Create(size: 3, sequences: 9)
            .Invariant("WITHIN-ONE-CYCLE", "Are we clear of all consumers? (Potentially a full cycle behind).",
                s => s.Next - s.MinCursor <= 3 + 1)
            .Exhaustive(maxStates: 2_000_000, writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
    }

    /// <summary>The gate is the whole protocol, so breaking it must break the invariant - otherwise the invariant is not
    /// the reason the design is safe and something else is carrying it. Off by one in the permissive direction lets a
    /// producer claim a slot one cycle too early, which is exactly the race the ring buffer exists to avoid.</summary>
    [Test]
    public async Task Loosening_The_Gate_By_One_Produces_A_Race()
    {
        var spec = DisruptorSpec.CreateWithGate(size: 3, sequences: 9, slack: 1);
        spec.Exhaustive(out var violation, TUnitX.WriteLine, maxStates: 2_000_000);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NO-DATA-RACES");
        TUnitX.WriteLine(violation.ToString(s => DisruptorSpec.Show(s, 3)));
    }

    /// <summary>Mutation testing the requirements rather than the design. Each fault is a way the implementation could
    /// be wrong, and the table says which requirement noticed. A fault nothing catches means a requirement is missing.</summary>
    [Test]
    public async Task Faults_Are_All_Caught()
    {
        var report = DisruptorSpec.CreateWithFaults(size: 3, sequences: 9).Faults(TUnitX.WriteLine, maxStates: 2_000_000);
        await Assert.That(report.Uncaught).IsEmpty();
        await Assert.That(report.CaughtBy("PublishBeforeWriting")).IsEqualTo("NO-DATA-RACES");
        await Assert.That(report.CaughtBy("HoldsSlotAfterPublishing")).IsEqualTo("NO-DATA-RACES");
    }
}
