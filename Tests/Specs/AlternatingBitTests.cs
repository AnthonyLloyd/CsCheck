namespace Tests.Specs;

using CsCheck;
using Order = AlternatingBitSpec.Order;
using Seq = AlternatingBitSpec.Seq;

/// <summary>Two textbook results, used as predictions rather than as illustrations: one bit is necessary, and one bit is
/// sufficient only over a channel that keeps order. Each is a configuration, and each has to come out the way the
/// literature says it does.</summary>
public class AlternatingBitTests
{
    /// <summary>The protocol as specified, over a channel that loses and duplicates but keeps order. Everything holds,
    /// and every requirement fired - including the three coverage ones, without which a run where the channel happened
    /// to behave would satisfy the lot and prove nothing.</summary>
    [Test]
    public async Task One_Bit_Over_A_Fifo_Channel_Is_Correct()
    {
        var report = AlternatingBitSpec.Create(Seq.OneBit, Order.Fifo).Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        // Four terminal states, one per way the run can finish with everything delivered and both wires empty.
        await Assert.That(report.TerminalStates).IsGreaterThan(0);
    }

    /// <summary>One bit is necessary. A receiver that delivers whatever arrives is the obvious implementation and the
    /// duplicate is the obvious consequence, but it takes the channel duplicating a frame to expose it - which is why
    /// this is a thing to prove rather than to reason about. The counterexample is the shortest such interleaving.</summary>
    [Test]
    public async Task Without_The_Bit_A_Frame_Is_Delivered_Twice()
    {
        var spec = AlternatingBitSpec.Create(Seq.None, Order.Fifo);
        spec.Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("DELIVERED-ONCE[0]");
        await Assert.That(violation.Detail).Contains("more than 1 times");
        TUnitX.WriteLine(violation.ToString(AlternatingBitSpec.Show));
    }

    /// <summary>And one bit is sufficient only if the channel keeps order. Over a channel that may deliver the second
    /// message in flight before the first, a stale frame carrying the bit the receiver is now waiting for is
    /// indistinguishable from the fresh one, and one bit cannot tell them apart. This is the result that says a sliding
    /// window needs a sequence number wide enough for the window, not one bit.</summary>
    [Test]
    public async Task Reordering_Defeats_One_Bit()
    {
        var spec = AlternatingBitSpec.Create(Seq.OneBit, Order.Reorder);
        spec.Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        TUnitX.WriteLine($"caught by {violation!.Id}: {violation.Detail}");
        TUnitX.WriteLine(violation.ToString(AlternatingBitSpec.Show));
    }

    /// <summary>The four combinations at a glance, which is the example being used to choose a design rather than to
    /// check one. Only the top left works.</summary>
    [Test]
    public async Task The_Design_Matrix()
    {
        foreach (var seq in new[] { Seq.OneBit, Seq.None })
            foreach (var order in new[] { Order.Fifo, Order.Reorder })
            {
                var report = AlternatingBitSpec.Create(seq, order).Exhaustive(out var violation, maxStates: 500_000);
                TUnitX.WriteLine($"{seq,-6} {order,-7}  {report.States,6:#,0} states {report.Transitions,7:#,0} "
                    + $"transitions  {(violation is null ? "holds" : "FAILS: " + violation.Id)}");
                await Assert.That(violation is null).IsEqualTo(seq == Seq.OneBit && order == Order.Fifo);
            }
    }

    /// <summary>Mutation testing the requirements, and it turns one of them in. <c>AtMost</c> catches the duplicate, which
    /// is what it is here for. But <b>no fault exercises STOP-AND-WAIT</b>, and the reason is a flaw in how it is written
    /// rather than in the protocol: its <c>until</c> is "the acknowledgement has moved the sender past this frame", which
    /// is the same condition that would let a second send happen at all. A step that both closes the scope and does the
    /// forbidden thing counts as a close, so the scope is always shut before <c>never</c> can look. The requirement is
    /// therefore true by construction and proves nothing, which is exactly the "candidate for being too weak" that the
    /// unexercised list exists to report. Left in and documented rather than quietly deleted, because it is the clearest
    /// demonstration in these examples of Faults finding a bad requirement instead of a bad design.</summary>
    [Test]
    public async Task Faults_Are_Caught_By_The_Requirement_Intended()
    {
        var report = AlternatingBitSpec.Create()
            // A receiver that flips its bit without delivering: the frame is lost silently.
            .Fault("FlipWithoutDelivering",
                (_, a) => a.JustDelivered >= 0,
                (b, a) => a with { JustDelivered = -1, Delivered = b.Delivered })
            // A receiver that delivers but forgets to flip, so the next copy is delivered again.
            .Fault("DeliverWithoutFlipping",
                (_, a) => a.JustDelivered >= 0,
                (b, a) => a with { ExpectedBit = b.ExpectedBit })
            // A sender that moves on without waiting for the acknowledgement.
            .Fault("SendsWithoutWaiting",
                (_, a) => a.JustSent >= 0,
                (_, a) => a with { NextFrame = a.NextFrame + 1, SenderBit = !a.SenderBit })
            .Faults(TUnitX.WriteLine, throwOnUncaught: false);
        TUnitX.WriteLine("");
        foreach (var r in report.Results) TUnitX.WriteLine($"{r.Fault,-24} caught by {r.CaughtBy ?? "NOTHING"}");
        TUnitX.WriteLine("unexercised: " + string.Join(", ", report.Unexercised));
        await Assert.That(report.Uncaught).IsEmpty();
        // The requirement this example exists for catches the duplicate, and nothing else gets there first.
        await Assert.That(report.CaughtBy("DeliverWithoutFlipping")).IsEqualTo("DELIVERED-ONCE[0]");
        // A sender that does not wait skips a frame, and ordering notices before anything else can.
        await Assert.That(report.CaughtBy("SendsWithoutWaiting")).IsEqualTo("IN-ORDER");
        // And the finding: STOP-AND-WAIT is true by construction, so no fault reaches it.
        await Assert.That(report.Unexercised).Contains("STOP-AND-WAIT[0]");
    }
}
