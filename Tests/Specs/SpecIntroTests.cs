namespace Tests.Specs;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsCheck;

/// <summary>An introduction to <c>Spec</c>, in one file. The subject is the state machine everyone has written: an
/// order that gets paid, shipped and delivered, or cancelled and refunded. It usually lives in a table on a wiki page
/// and gets implemented as a switch, and it always has a hole in it somewhere.
/// <para>Nothing here needs abstracting. The state is a three field record and the space closes at seven states and six
/// transitions, so you can check the tool's answer by hand - which is the point of reading this one first.</para>
/// <para>The worked examples that follow this one (FixEngine, RefreshCache, Fencing, BlockingQueue, AlternatingBit,
/// Disruptor, TerminationDetection) are where the technique gets
/// interesting and where the abstraction choices start to matter.</para></summary>
public class SpecIntroTests
{
    public enum Status { New, Paid, Shipped, Delivered, Cancelled }

    /// <summary>The model state. It must be immutable with value equality - a record or record struct - because
    /// <c>Exhaustive</c> compares and hashes states to know when it has seen one before. Money is one unit, so
    /// <c>Paid</c> and <c>Refunded</c> are 0 or 1: the requirements below are about the relationship between them,
    /// not about the amount - except REFUND-IS-ONE-STEP, which turns out to be about the amount after all. Give
    /// <c>Pay</c> two amounts and it is false in three steps. docs/Spec.md works that through, because choosing a
    /// domain that could falsify the requirement rather than one that looks realistic is most of the skill.</summary>
    public readonly record struct Order(Status Status, int Paid, int Refunded)
    {
        public bool Settled => Refunded == Paid;

        public override string ToString() => $"{Status,-9} paid={Paid} refunded={Refunded}";
    }

    /// <summary>The specification. Actions say what can happen and when; requirements say what must be true when it
    /// does. The guards are the state machine - there is no separate transition table.</summary>
    static Spec<Order> Create(bool refundable = true)
        => Spec.From(new Order(Status.New, 0, 0))
        .Print(o => o.ToString())

        .Action("Pay", o => o.Status == Status.New,
            o => o with { Status = Status.Paid, Paid = 1 })
        .Action("Ship", o => o.Status == Status.Paid,
            o => o with { Status = Status.Shipped })
        .Action("Deliver", o => o.Status == Status.Shipped,
            o => o with { Status = Status.Delivered })
        .Action("Cancel", o => o.Status is Status.New or Status.Paid,
            o => o with { Status = Status.Cancelled })
        .Action("Refund", o => refundable && o.Status == Status.Cancelled && o.Refunded < o.Paid,
            o => o with { Refunded = o.Refunded + 1 })

        // Where the order is meant to end up. Anything else with nothing left to do is a dead end the design did not
        // intend, and Exhaustive reports it without a requirement being written for it.
        .Terminal(o => o.Status == Status.Delivered || (o.Status == Status.Cancelled && o.Settled))

        // Must hold in every reachable state.
        .Invariant("NO-OVER-REFUND",
            "Never refund more than was paid.",
            o => o.Refunded <= o.Paid)

        // Must hold in at least one reachable state. The dual of an invariant, and the one requirement that catches a
        // model so over-constrained that everything else passes because nothing interesting can happen.
        .Reachable("CAN-DELIVER",
            "A happy path exists: an order can be paid, shipped and delivered.",
            o => o.Status == Status.Delivered)
        .Reachable("CAN-REFUND",
            "A paid order can be cancelled and the money returned.",
            o => o.Status == Status.Cancelled && o.Paid == 1 && o.Refunded == 1)

        // Must never be true of any step. Both states are available, so a requirement can talk about what changed.
        .Never("NO-SHIP-UNPAID",
            "Goods only leave once the money has arrived.",
            (_, after) => after.Status is Status.Shipped or Status.Delivered && after.Paid == 0)
        .Never("NO-CANCEL-AFTER-SHIP",
            "Once goods are on their way the order cannot be cancelled; that is a return, not a cancellation.",
            (before, after) => before.Status is Status.Shipped or Status.Delivered && after.Status == Status.Cancelled)
        .Never("NO-MONEY-VANISHING",
            "What the customer paid is never quietly forgotten. It is refunded or it is kept, never dropped.",
            (before, after) => after.Paid < before.Paid)

        // Whenever this action runs, this must hold over that step.
        .Rule("REFUND-IS-ONE-STEP",
            "A refund settles the order in a single movement of money.",
            on: "Refund",
            then: (before, after) => after.Refunded == before.Refunded + 1 && after.Settled)

        // Deliberate defects, so the requirements above can be shown to be strong enough to catch something.
        .Fault("payment is not recorded",
            (before, after) => after.Paid > before.Paid,
            (before, after) => after with { Paid = before.Paid })
        .Fault("cancelling discards the payment",
            (_, after) => after.Status == Status.Cancelled,
            (_, after) => after with { Paid = 0 })
        .Fault("refund pays out twice",
            (before, after) => after.Refunded > before.Refunded,
            (before, after) => after with { Refunded = before.Refunded + 2 })
        .Fault("cancel is allowed too late",
            (before, _) => before.Status == Status.Shipped,
            (_, after) => after with { Status = Status.Cancelled })
        .Fault("refund does not settle the order",
            (before, after) => after.Refunded > before.Refunded,
            (before, after) => after with { Refunded = before.Refunded });

    /// <summary>Enumerate every reachable state and check every requirement on every transition out of every one of
    /// them. When the frontier empties the state space is closed, so this is a proof for the model rather than a
    /// sample of it, and the report is the certificate.
    /// <para>Read the report as well as the assertions. Triggered says how often each requirement's antecedent actually</para>
    /// fired - a NEVER there means the requirement passed vacuously and proves nothing.</summary>
    [Test]
    public async Task Exhaustive_Proof()
    {
        var report = Create().Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        // Pin the size of the space too. Every assertion above has the form "no counterexample was found", which a
        // search that explored too little also satisfies, so on its own a change that dropped a whole class of
        // successor would leave this green while proving less. Small enough here to check by hand.
        await Assert.That(report.States).IsEqualTo(7);
        await Assert.That(report.Transitions).IsEqualTo(6);
    }

    /// <summary>Mutation testing for the specification itself. Each planted defect is injected in turn and the state
    /// space re-explored, and the table says which requirement caught it and in how few steps. A defect that nothing
    /// catches means a requirement is missing; a defect caught by a different requirement than you expected means the
    /// defect or the requirement is not what you thought.</summary>
    [Test]
    public void Faults_Are_All_Caught()
    {
        Create().Faults(TUnitX.WriteLine);
    }

    /// <summary>Forget to let a cancelled order be refunded - an omission, not a wrong answer - and a paid order that
    /// is cancelled can reach a state it can never leave with the customer's money still held.
    /// <para>Two independent signals catch it. CAN-REFUND is now provably unreachable, because the state space closed
    /// without it ever holding; that is what Reachable is for. And the deadlock count is 1, which costs no requirement
    /// at all - Exhaustive knows which states have nothing enabled, and Terminal said which of those were intended.
    /// The second signal is the more interesting one, because nobody writes a requirement for a transition they forgot.</para>
    /// <para>Refund and REFUND-IS-ONE-STEP report NEVER here, which is correct: with the action disabled there is nothing</para>
    /// for them to do. That is what the coverage table is for.</summary>
    [Test]
    public async Task Missing_Transition_Is_A_Dead_End()
    {
        var report = Create(refundable: false).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(1);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("CAN-REFUND");
        // The count says a dead end exists; DeadlockTrace says which one, and it is the whole point of the example -
        // a cancelled order still holding the money, with nothing left to do.
        await Assert.That(report.DeadlockTrace).IsNotNull();
        await Assert.That(report.DeadlockTrace).Contains("Cancelled paid=1 refunded=0");
        await Assert.That(report.DeadlockTrace).Contains("no action enabled");
    }

    /// <summary>Seven states is small enough to look at, so this is the one example where a picture beats a table.
    /// <c>Dot</c> draws the reachable graph; pipe it through <c>dot -Tsvg</c>. The dead end in the version without a
    /// refund is filled rather than doubled, which is the whole finding of the test above, visible at a glance.</summary>
    [Test]
    public async Task State_Graph_As_Dot()
    {
        var dot = Create().Dot();
        TUnitX.WriteLine(dot);
        await Assert.That(dot).StartsWith("digraph spec {");
        // Seven nodes and six edges, matching the proof, and three intended ends drawn doubled: delivered, cancelled
        // after a refund, and cancelled before paying - which is settled too, and which reading the picture corrected.
#pragma warning disable SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
        await Assert.That(Regex.Count(dot, @"\[label=""(New|Paid|Shipped|Delivered|Cancelled)")).IsEqualTo(7);
        await Assert.That(Regex.Count(dot, " -> n")).IsEqualTo(6);
        await Assert.That(Regex.Count(dot, "doublecircle")).IsEqualTo(3);
        await Assert.That(dot).DoesNotContain("fillcolor");

        // Without the refund the cancelled order is a dead end, so it is filled instead.
        var stuck = Create(refundable: false).Dot();
        TUnitX.WriteLine(stuck);
        await Assert.That(Regex.Count(stuck, "fillcolor")).IsEqualTo(1);
#pragma warning restore SYSLIB1045 // Convert to 'GeneratedRegexAttribute'.
    }

    /// <summary>The same specification as a random walk. For a model this small it adds nothing over the proof, but it
    /// is what you fall back on when a model is too large to close, and it needs no change to the specification.</summary>
    [Test]
    public async Task Sample()
    {
        var report = Create().Sample(TUnitX.WriteLine, maxSteps: 12, iter: 5_000);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }
}
