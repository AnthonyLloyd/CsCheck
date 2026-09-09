namespace Tests.Specs;

using System;
using System.Linq;
using CsCheck;

/// <summary>The FIX 4.4 session core, specified once and then checked four ways: proved exhaustively, sampled
/// randomly, mutation tested to show the requirements are strong enough, and used to check a hand written engine
/// conforms to it.</summary>
public class FixEngineTests
{
    /// <summary>Enumerate every reachable state of the session and check all 31 requirements on every transition out
    /// of every one of them. When the frontier empties the space is closed, so this is a proof for the abstracted
    /// model rather than a sample of it - including the two bounded response requirements, whose outstanding
    /// deadlines are carried in the search state.
    /// <para>The size of the space is pinned as well. Every other assertion here has the form "no counterexample was
    /// found", which a search that explored too little also satisfies, so without this a change that dropped a whole
    /// class of successor would leave the test green while proving strictly less. A model change that legitimately</para>
    /// moves these numbers should update them in the same commit, deliberately.</summary>
    [Test]
    public async Task Exhaustive_Proof()
    {
        var report = FixEngineSpec.Create().Exhaustive(TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        await Assert.That(report.States).IsEqualTo(2_438);
        await Assert.That(report.Transitions).IsEqualTo(51_569);
    }

    /// <summary>The same specification driven as a random walk. This is what you run when the model is too big to
    /// close, and it is the mode that scales to an unabstracted model.</summary>
    [Test]
    public async Task Sample()
    {
        var report = FixEngineSpec.Create().Sample(TUnitX.WriteLine, maxSteps: 30, iter: 20_000);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>Mutation testing for the specification itself. Each planted defect is injected in turn and the state
    /// space re-explored; the table shows which requirement caught it and how many steps the shortest counterexample
    /// took. Faults throws if any defect escapes every requirement, so this fails when a requirement is missing.
    /// <para>The two pairings asserted below are the ones worth pinning. Being caught by something is not enough: a fault
    /// caught by the wrong requirement passes while leaving the intended one unproven, which is what happened to the</para>
    /// test request fault before it was rewritten to perturb only the termination.</summary>
    [Test]
    public async Task Faults_Are_All_Caught()
    {
        var report = FixEngineSpec.Create().Faults(TUnitX.WriteLine);
        await Assert.That(report.CaughtBy("logon too low is not fatal")).IsEqualTo("LOGON-TOO-LOW");
        await Assert.That(report.CaughtBy("bad OrigSendingTime ignored instead of rejected")).IsEqualTo("POSSDUP-BAD-ORIG");
        // Both of these drifted onto the wrong requirement when the outbound sequence number was added, so they are
        // pinned: the first to HB-KEEPALIVE rather than the arithmetic, the second to the one requirement that is
        // about a reconnect rather than to monotonicity.
        await Assert.That(report.CaughtBy("no heartbeat when idle")).IsEqualTo("HB-KEEPALIVE");
        await Assert.That(report.CaughtBy("sequence numbers drift on reconnect")).IsEqualTo("SEQNUM-PERSISTS");
        // The pair that shows what the whole-trace Precedes does not give you: the same defect one connection later
        // is invisible to it, and only the per connection requirement has it.
        await Assert.That(report.CaughtBy("app sent before logon")).IsEqualTo("NO-APP-BEFORE-LOGON");
        await Assert.That(report.CaughtBy("app sent before the second logon")).IsEqualTo("NO-APP-UNTIL-LOGGED-ON");
        await Assert.That(report.Uncaught).IsEmpty();
    }

    /// <summary>The interesting negative result. A bare SequenceReset is verified with both sequence number checks
    /// off, so it is accepted - and therefore refreshes the liveness timers - before NewSeqNo is examined and the
    /// message rejected. A counterparty trickling invalid SequenceResets refreshes the timers with messages that do
    /// nothing, so the test request timeout never fires and a gap can stay open indefinitely. Nothing in the session
    /// layer bounds it. Adding the requirement any engineer would assume holds produces a six step counterexample.</summary>
    [Test]
    public async Task Gap_Is_Not_Bounded()
    {
        FixEngineSpec.Create()
        .Response("GAP-RESOLVED",
            "A gap, once detected, is filled or the session is terminated.",
            trigger: (b, a) => a.GapOpen && !b.GapOpen,
            response: (b, a) => !a.GapOpen || a.Status == FixEngine.ConnectionStatus.Disconnected,
            within: FixEngine.Interval * 2, per: "Tick")
        .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("GAP-RESOLVED");
        TUnitX.WriteLine(violation.ToString(s => s.ToString()));
    }

    /// <summary>Conformance. The same random walk drives the specification and a hand written imperative engine, and
    /// every step compares what the engine did with what the specification says. The engine has one planted defect,
    /// so this is expected to fail and the assertion is on the shrunk counterexample.</summary>
    [Test]
    public async Task Conforms_To_Spec()
    {
        static bool Apply(FixEngine e, Transition<FixEngineSpec.State> t)
        {
            switch (t.Action)
            {
                case "Recv": e.Inbound(FixEngineSpec.Inbound[t.ArgIndex]); break;
                case "Tick": e.Tick(); break;
                case "SendApp": e.SendApp(); break;
                case "SendLogout": e.SendLogout(); break;
                case "Reconnect": e.Reconnect(); break;
                default: e.Drop(); break;
            }
            return e.Status == t.After.Status && e.Sent == t.After.Sent
                && e.Expect == t.After.Expect && e.Next == t.After.Next && e.GapOpen == t.After.GapOpen;
        }
        var message = Assert.Throws<CsCheckException>(
            () => FixEngineSpec.Create().Conform(() => new FixEngine(), Apply, TUnitX.WriteLine, iter: 100_000))!.Message;
        TUnitX.WriteLine(message);
        await Assert.That(message).Contains("TooLowDup");
    }

    /// <summary>The specification is also just a generator, so a trace can be reused in an ordinary Sample. Here it
    /// pins down the shape of the state space that the proof above covers: a disconnected session does nothing but
    /// reconnect, and sequence numbers only ever climb apart from a reset.</summary>
    [Test]
    public void Traces_Are_Well_Formed()
    {
        FixEngineSpec.Create().GenTrace(1, 20)
        .Sample(trace =>
        {
            foreach (var step in trace.Steps)
            {
                if (step.Before.Status == FixEngine.ConnectionStatus.Disconnected && step.Action != "Reconnect") return false;
                if (step.Before.Expect > step.After.Expect && !step.After.WasReset) return false;
            }
            return trace.Steps.Length == 0 || trace.Steps[0].Before == FixEngineSpec.State.Connected;
        }, iter: 10_000);
    }

    /// <summary>How much of the specification each inbound case is responsible for. Not an assertion, a map: it says
    /// which of the 20 FIX inbound cases actually drive behaviour and which are quietly handled the same way.</summary>
    [Test]
    public void Inbound_Classify()
    {
        FixEngineSpec.Create().GenTrace(1, 12)
        .Sample(trace =>
        {
            var last = trace.Steps.LastOrDefault();
            return last.Action == "Recv"
                ? string.Concat(FixEngineSpec.Inbound[last.ArgIndex].ToString(), "/", last.After.Status.ToString())
                : string.Concat(last.Action ?? "none", "/", last.After.Status.ToString());
        }, TUnitX.WriteLine, iter: 20_000);
    }
}
