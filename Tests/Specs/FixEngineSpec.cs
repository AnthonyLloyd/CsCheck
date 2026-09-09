namespace Tests.Specs;

using System;
using System.Numerics;
using CsCheck;
using static Tests.Specs.FixEngine;

/// <summary>The session establishment, sequencing and liveness core of the FIX 4.4 session layer (acceptor side), as
/// an executable specification of <see cref="FixEngine"/>. Not the whole session layer: the scope is stated at the
/// bottom of this comment and is narrower than the phrase "session layer" would suggest. Checked against QuickFIX/n
/// Session.cs and SessionState.cs, which is why some rules below cite it.
/// <para>Two words to be careful with. <see cref="State"/> is the complete valuation of every variable at one instant -
/// what model checkers mean by state, and what <c>Spec&lt;S&gt;</c> takes - while the single mode within it is
/// <see cref="ConnectionStatus"/>, which is what an FSM library would have called the state. And the last
/// inbound message and the messages emitted are themselves fields of <see cref="State"/>, which is what makes every
/// requirement a predicate over a pair of states.</para>
/// <para>A session outlives its connections, so <see cref="ConnectionStatus.Disconnected"/> is not the end: one
/// <see cref="State.Reconnect"/> is modelled, carrying the sequence numbers across. That is what makes the two
/// directions of a reset distinguishable at all, and it is why several requirements have to stand aside for
/// <see cref="State.WasReset"/>.</para>
/// <para>Two abstractions make the space finite. Sequence numbers become their <see cref="Seq"/> relation to the number
/// expected, which is how the session layer's own rules are worded, but this loses NewSeqNo: a SequenceReset raises
/// <see cref="State.Expect"/> by one rather than setting it. The counters also saturate at <see cref="Cap"/>, which a
/// Logon answered with a ResendRequest reaches immediately, so OUTBOUND-ADVANCES checks its arithmetic below the cap
/// and above it only that the numbers stop moving. And the clocks are <see cref="Interval"/> ticks saturating at
/// <see cref="Cap"/>, collapsing four constants QuickFIX keeps independent (1x HeartBtInt to send a heartbeat, 1.2x
/// to send a TestRequest, 2.4x to time out, fixed seconds for logon and logout), so the ordering of the timing rules
/// is checked and their ratios are not.</para>
/// <para>Out of scope, so read nothing here as evidence about it: what a resend actually contains, beyond that one happens
/// and that outbound numbers are consumed one per message; PossDupFlag and OrigSendingTime on messages this side
/// resends; administrative messages replaced by SequenceReset-GapFill; SendingTime accuracy; CompID validation; and
/// session level Rejects other than the two below.</para></summary>
public static class FixEngineSpec
{
    /// <summary>MsgSeqNum of an inbound message relative to the expected number. For <see cref="In.SeqReset"/> this
    /// is the relation of NewSeqNo instead, since a bare SequenceReset ignores MsgSeqNum. <c>TooLowDup</c> is
    /// PossDupFlag=Y with a valid OrigSendingTime; <c>DupBadOrig</c> is PossDupFlag=Y with OrigSendingTime missing
    /// or later than SendingTime, which the session layer requires be rejected rather than ignored.</summary>
    public enum Seq { Expected, TooHigh, TooLow, TooLowDup, DupBadOrig }

    /// <summary>An inbound message for the spec domain, expressed as kind and sequence relation rather than kind and
    /// raw integer. This is the spec's own vocabulary; the engine's <see cref="FixEngine.Msg"/> carries a concrete
    /// sequence number and PossDup flags, and the conformance test's <c>Apply</c> function translates between the
    /// two.</summary>
    public readonly record struct Msg(In Kind, Seq Seq)
    {
        public override string ToString() => Seq == Seq.Expected ? Kind.ToString() : $"{Kind} {Seq}";
    }

    /// <summary>The inbound cases the session must handle. This list is the conformance matrix: one entry per
    /// (message kind, sequence relation) pair that the FIX session layer gives a rule for.</summary>
    public static readonly Msg[] Inbound =
    [
        new(In.Logon, Seq.Expected), new(In.Logon, Seq.TooHigh), new(In.Logon, Seq.TooLow),
        new(In.LogonReset, Seq.Expected),
        // DupBadOrig is carried only by App. QuickFIX skips the OrigSendingTime check entirely for SequenceReset when
        // RequiresOrigSendingTime is off, so putting it on GapFill would need that carve out modelled too.
        new(In.App, Seq.Expected), new(In.App, Seq.TooHigh), new(In.App, Seq.TooLow), new(In.App, Seq.TooLowDup),
        new(In.App, Seq.DupBadOrig),
        new(In.Heartbeat, Seq.Expected), new(In.Heartbeat, Seq.TooHigh),
        new(In.TestRequest, Seq.Expected),
        new(In.ResendRequest, Seq.Expected),
        new(In.GapFill, Seq.Expected), new(In.GapFill, Seq.TooLowDup),
        new(In.SeqReset, Seq.TooHigh), new(In.SeqReset, Seq.TooLow),
        // FIX 4.4 requires a Logout to be accepted regardless of sequence number. Both entries verify the engine
        // does not misapply the fatal sequence error path (SEQ-TOO-LOW-FATAL) to an incoming Logout.
        new(In.Logout, Seq.Expected), new(In.Logout, Seq.TooLow),
        new(In.Garbled, Seq.Expected),
    ];

    /// <summary>The model state. A record struct, so <c>Check.Exhaustive</c> can hash and compare states by value
    /// and close the state space.</summary>
    public readonly record struct State(ConnectionStatus Status, int Expect, int Next, bool Reconnected, bool GapOpen,
        int Queued, int Idle, int Quiet, bool TestSent, In Recv, Seq RecvSeq, Out Sent)
    {
        /// <summary>A freshly accepted TCP connection, waiting for a Logon.</summary>
        public static readonly State Connected = new(ConnectionStatus.AwaitingLogon, 1, 1, false, false, 0, 0, 0, false, In.Nothing, Seq.Expected, Out.None);

        /// <summary>Logged on for the purpose of processing inbound messages. Stays true while a Logout we sent is
        /// outstanding, which is what QuickFIX's IsLoggedOn does.</summary>
        public bool Up => Status is ConnectionStatus.LoggedOn or ConnectionStatus.LogoutSent;
        public bool Got(In kind) => Recv == kind;
        public bool Got(In kind, Seq seq) => Recv == kind && RecvSeq == seq;
        public bool Put(Out o) => (Sent & o) != 0;
        /// <summary>A message arrived and parsed, so the counterparty is known to be alive.</summary>
        public bool Heard => Recv is not In.Nothing and not In.Garbled;
        /// <summary>An accepted Logon carrying ResetSeqNumFlag=Y: the one transition in the session layer that lowers
        /// a sequence number, so the one the arithmetic and monotonicity requirements have to stand aside for. A
        /// LogonReset that was refused is not this and is still held to them.</summary>
        public bool WasReset => Recv == In.LogonReset && Status == ConnectionStatus.LoggedOn;

        State Clock() => this with { Recv = In.Nothing, RecvSeq = Seq.Expected, Sent = Out.None };
        /// <summary>A message arrived off the wire. The inbound clock is deliberately not touched here.</summary>
        State Arrive(In kind, Seq seq) => this with { Recv = kind, RecvSeq = seq, Sent = Out.None };
        /// <summary>The message passed validation and was dispatched, which is the only thing that counts as evidence
        /// the counterparty is alive. QuickFIX assigns LastReceivedTimeDT and clears TestRequestCounter at the end of
        /// Verify, after every early return, so a queued or ignored message does not refresh the timers.</summary>
        State Accept() => this with { Quiet = 0, TestSent = false };
        // Once a Logout is outstanding Idle is the countdown to giving up on the confirming Logout, so sending must
        // not reset it. Measuring that timeout from the last message sent (as QuickFIX does) lets a counterparty
        // which keeps eliciting replies hold a half closed session open forever - found by Exhaustive at depth 6.
        State Emit(Out o) => this with { Sent = Sent | o, Next = Math.Min(Next + 1, Cap),
                                         Idle = Status == ConnectionStatus.LogoutSent ? Idle : 0 };
        State Consume() => this with { Expect = Math.Min(Expect + 1, Cap) };
        State Fill() => this with { GapOpen = false, Queued = 0 };
        State Queue() => this with { GapOpen = true, Queued = Math.Min(Queued + 1, 2) };
        State Age() => this with { Idle = Math.Min(Idle + 1, Cap), Quiet = Math.Min(Quiet + 1, Cap) };
        State Terminate() => this with { Status = ConnectionStatus.Disconnected, GapOpen = false, Queued = 0, TestSent = false, Idle = 0, Quiet = 0 };

        public State Inbound(Msg m)
        {
            var s = Arrive(m.Kind, m.Seq);

            if (m.Kind == In.Garbled) return s;

            if (m.Kind == In.Logout)
                return Status == ConnectionStatus.LogoutSent ? s.Accept().Terminate()
                                                        : s.Accept().Emit(Out.Logout).Terminate();

            if (m.Kind is In.Logon or In.LogonReset)
            {
                if (Up) return s.Emit(Out.Logout).Terminate();
                if (m.Seq == Seq.TooLow) return s.Emit(Out.Logout).Terminate();
                // ResetSeqNumFlag=Y resets both directions, and before the reply rather than after: QuickFIX calls
                // SessionState.Reset, which sets NextSenderMsgSeqNum and NextTargetMsgSeqNum to 1, and only then
                // generates the Logon response - so that response carries sequence number 1.
                if (m.Kind == In.LogonReset) s = s with { Expect = 1, Next = 1 };
                // A Logon is verified with the too high check off, so it is accepted and then the gap is filled.
                s = s.Accept().Emit(Out.Logon) with { Status = ConnectionStatus.LoggedOn };
                if (m.Kind == In.LogonReset) return s.Fill().Consume();
                return m.Seq == Seq.TooHigh ? s.Emit(Out.ResendRequest).Queue() : s.Consume();
            }

            if (!Up) return s.Terminate();

            // A bare SequenceReset is verified with both sequence checks off, so it is accepted whatever NewSeqNo says.
            if (m.Kind == In.SeqReset)
                return m.Seq == Seq.TooHigh ? s.Accept().Fill().Consume() : s.Accept().Emit(Out.Reject);

            switch (m.Seq)
            {
                // DoPossDup rejects and returns before Verify reaches the point where the liveness timers are
                // refreshed, so a rejected duplicate is no evidence the counterparty is alive.
                case Seq.DupBadOrig:
                    return s.Emit(Out.Reject);
                case Seq.TooLowDup:
                    return s;
                case Seq.TooLow:
                    return s.Emit(Out.Logout).Terminate();
                case Seq.TooHigh:
                    return GapOpen ? s.Queue() : s.Emit(Out.ResendRequest).Queue();
                default:
                    s = s.Accept();
                    s = m.Kind switch
                    {
                        In.TestRequest => s.Emit(Out.Heartbeat),
                        In.ResendRequest => s.Emit(Out.Resend),
                        _ => s,
                    };
                    return GapOpen ? s.Fill().Consume() : s.Consume();
            }
        }

        public State Tick()
        {
            var s = Clock();
            // An unanswered TestRequest has to be tested before the logout timeout, or a dead counterparty holds the
            // socket open for longer than a live one would. QuickFIX checks its LogoutTimedOut first and so takes a
            // tick longer, which is a divergence from this specification rather than a rule of the protocol.
            if (TestSent && Quiet >= Interval) return s.Terminate();
            if (Status is ConnectionStatus.AwaitingLogon or ConnectionStatus.LogoutSent)
                return Idle >= Interval ? s.Terminate() : s.Age();
            if (Quiet >= Interval) return (s.Emit(Out.TestRequest) with { TestSent = true }).Age();
            if (Idle >= Interval) return s.Emit(Out.Heartbeat).Age();
            return s.Age();
        }

        public State SendApp() => Clock().Emit(Out.App);
        public State SendLogout() => Clock().Emit(Out.Logout) with { Status = ConnectionStatus.LogoutSent };
        public State TransportDrop() => Clock().Terminate();
        /// <summary>A new connection for the same session, carrying the sequence numbers over. One reconnect is
        /// modelled rather than any number: it is enough to reach a Logon with the counters above 1, which is the
        /// only way the two directions of a reset can be told apart, and a second would revisit the same rules.</summary>
        public State Reconnect() => Connected with { Expect = Expect, Next = Next, Reconnected = true };

        public override string ToString()
            => string.Concat(
                Status.ToString().PadRight(13), " exp=", Expect.ToString(), " out=", Next.ToString(),
                GapOpen ? " gap" : "", Queued > 0 ? "+" + Queued.ToString() : "",
                " idle=", Idle.ToString(), " quiet=", Quiet.ToString(), TestSent ? " tr?" : "",
                "  << ", Recv == In.Nothing ? "-" : new Msg(Recv, RecvSeq).ToString(),
                "  >> ", Sent == Out.None ? "-" : Sent.ToString());
    }

    /// <summary>The specification: 20 inbound cases, 5 local events, and the requirements the session layer places on
    /// them. Each requirement carries the rule it comes from; swap the ids for your own clause numbering and the
    /// coverage table becomes a traceability matrix.</summary>
    public static Spec<State> Create()
        => Spec.From(State.Connected)
        .Print(s => s.ToString())

        // ── what can happen to a session ────────────────────────────────────────────────────────────────────
        // Weights only steer the random walk; Exhaustive enumerates all of these regardless. Drop is always enabled,
        // so left at the same weight as the rest it eats half the sampling budget.
        .Action("Recv", Inbound, (s, _) => s.Status != ConnectionStatus.Disconnected, (s, m) => s.Inbound(m), weight: 30)
        .Action("Tick", s => s.Status != ConnectionStatus.Disconnected, s => s.Tick(), weight: 20)
        .Action("SendApp", s => s.Status == ConnectionStatus.LoggedOn, s => s.SendApp(), weight: 5)
        .Action("SendLogout", s => s.Status == ConnectionStatus.LoggedOn, s => s.SendLogout(), weight: 2)
        .Action("Drop", s => s.Status != ConnectionStatus.Disconnected, s => s.TransportDrop(), weight: 1)
        .Action("Reconnect", s => s.Status == ConnectionStatus.Disconnected && !s.Reconnected, s => s.Reconnect(), weight: 1)
        .Terminal(s => s.Status == ConnectionStatus.Disconnected)

        // ── logon ───────────────────────────────────────────────────────────────────────────────────────────
        .Invariant("EXPECT-POSITIVE",
            "MsgSeqNum: value must be positive",
            s => s.Expect >= 1)
        // The guard against a model so over-constrained that everything below passes because nothing can happen. Cheap
        // to state and the one requirement whose failure means the rest of the run proved nothing.
        .Reachable("CAN-LOG-ON",
            "A session can reach the logged on state at all.",
            s => s.Status == ConnectionStatus.LoggedOn)
        .Rule("LOGON-FIRST",
            "The Logon message must be the first message sent by the initiator and the first message received by "
            + "the acceptor. Receipt of any other message type before a Logon terminates the connection.",
            when: (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Heard && a.Recv is not In.Logon and not In.LogonReset and not In.Logout,
            then: (_, a) => a.Status == ConnectionStatus.Disconnected)
        .Rule("LOGON-REPLY",
            "Upon receipt of a valid Logon the acceptor must respond with a Logon message.",
            when: (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Recv is In.Logon or In.LogonReset && a.RecvSeq != Seq.TooLow,
            then: (_, a) => a.Put(Out.Logon) && a.Status == ConnectionStatus.LoggedOn)
        .Rule("LOGON-TOO-HIGH",
            "If the MsgSeqNum of the Logon is higher than expected, respond with a Logon and then send a "
            + "ResendRequest. The session is established: it is not terminated for the gap.",
            when: (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Got(In.Logon, Seq.TooHigh),
            then: (_, a) => a.Put(Out.Logon) && a.Put(Out.ResendRequest) && a.Status == ConnectionStatus.LoggedOn)
        .Rule("LOGON-TOO-LOW",
            "A Logon whose MsgSeqNum is lower than expected is the fatal too low case like any other message: send a "
            + "Logout and terminate. QuickFIX/n reaches this by calling Verify with the too low check left on.",
            when: (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Got(In.Logon, Seq.TooLow),
            then: (_, a) => a.Put(Out.Logout) && a.Status == ConnectionStatus.Disconnected)
        // This is a policy decision, not a rule I can cite. FIX 4.4 says Logon must be the first message received,
        // but names no behaviour for a second one, and QuickFIX/n does not reject it - NextLogon runs its normal path
        // again, sends another Logon response and calls OnLogon a second time. Terminating is the stricter reading.
        .Rule("LOGON-DUPLICATE",
            "A Logon received while a session is already established is treated as an error by this engine and the "
            + "connection is terminated, rather than being processed as a second logon.",
            when: (b, a) => b.Up && a.Recv is In.Logon or In.LogonReset,
            then: (_, a) => a.Status == ConnectionStatus.Disconnected)
        .Never("NO-REJECT-BEFORE-LOGON",
            "A Reject may not be sent until a Logon has been received; there is no session to reject on.",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Put(Out.Reject))
        // These two look like one requirement and are not, which is the clearest thing in this file about what a
        // whole-trace history form buys you. Precedes asks only that some Logon came first, so once the session has
        // logged on once it is satisfied for the rest of the trace, including on a later connection that has not
        // logged on yet. Saying it per connection needs the state, not the history. Dwyer's patterns have scopes for
        // exactly this; these forms do not, so the second requirement is the workaround.
        .Precedes("NO-APP-BEFORE-LOGON",
            "No application message is sent before the first Logon exchange of the session has completed.",
            first: (_, a) => a.Put(Out.Logon),
            second: (_, a) => a.Put(Out.App))
        .Never("NO-APP-UNTIL-LOGGED-ON",
            "Application messages may not be exchanged until the Logon exchange has completed, and a new connection "
            + "must complete its own before the session resumes.",
            (b, a) => !b.Up && a.Put(Out.App))

        // ── inbound sequence number handling ────────────────────────────────────────────────────────────────
        // Queued saturates at 2 in this abstraction, so "one more is queued" is stated as Min(before + 1, 2).
        .Rule("SEQ-TOO-HIGH-QUEUE",
            "MsgSeqNum higher than expected: the message is queued and is not processed. The expected sequence "
            + "number is not advanced.",
            when: (b, a) => b.Up && a.Heard && a.RecvSeq == Seq.TooHigh && a.Recv is not In.Logon and not In.SeqReset and not In.Logout,
            then: (b, a) => a.GapOpen && a.Queued == Math.Min(b.Queued + 1, 2) && a.Expect == b.Expect
                         && a.Quiet == b.Quiet && a.TestSent == b.TestSent)
        .Rule("SEQ-TOO-HIGH-RESEND",
            "MsgSeqNum higher than expected: send a ResendRequest for the missing range.",
            when: (b, a) => b.Up && a.Heard && !b.GapOpen && a.RecvSeq == Seq.TooHigh && a.Recv is not In.Logon and not In.SeqReset and not In.Logout,
            then: (_, a) => a.Put(Out.ResendRequest))
        .Never("NO-DUPLICATE-RESEND",
            "Do not send a second ResendRequest while a ResendRequest is already outstanding.",
            (b, a) => b.GapOpen && a.Put(Out.ResendRequest))
        .Rule("SEQ-TOO-LOW-FATAL",
            "MsgSeqNum lower than expected without PossDupFlag set to Y is a fatal error: send a Logout with the "
            + "text \"MsgSeqNum too low, expecting X but received Y\" and terminate the connection.",
            when: (b, a) => b.Up && a.Heard && a.RecvSeq == Seq.TooLow && a.Recv is not In.Logout and not In.SeqReset,
            then: (_, a) => a.Put(Out.Logout) && a.Status == ConnectionStatus.Disconnected)
        .Rule("POSSDUP-IGNORED",
            "PossDupFlag set to Y with MsgSeqNum lower than expected and a valid OrigSendingTime: the message has "
            + "already been processed and is ignored. This is a rule of the established session; before a Logon, "
            + "LOGON-FIRST wins.",
            when: (b, a) => b.Up && a.Heard && a.RecvSeq == Seq.TooLowDup,
            then: (b, a) => a.Sent == Out.None && a.Expect == b.Expect && a.Status == b.Status
                         && a.Quiet == b.Quiet && a.TestSent == b.TestSent)
        .Rule("POSSDUP-BAD-ORIG",
            "PossDupFlag set to Y with OrigSendingTime missing, or later than SendingTime, must be answered with a "
            + "session level Reject and the message must not be processed. Ignoring it silently, which is what a "
            + "single TooLowDup case would have forced this specification to require, is wrong.",
            when: (b, a) => b.Up && a.RecvSeq == Seq.DupBadOrig,
            then: (b, a) => a.Put(Out.Reject) && a.Expect == b.Expect && a.Status == b.Status
                         && a.Quiet == b.Quiet && a.TestSent == b.TestSent)
        .Rule("GARBLED-IGNORED",
            "Garbled message received: ignore it. Do not increment the expected sequence number and do not send a "
            + "Reject, because the message could not be trusted to identify itself.",
            when: (_, a) => a.Got(In.Garbled),
            then: (b, a) => a.Sent == Out.None && a.Expect == b.Expect && a.Status == b.Status && a.Quiet == b.Quiet)
        // Both of these hold across a reconnect, which carries the numbers over, and both stand aside for a reset.
        .Never("EXPECT-MONOTONIC",
            "The expected incoming sequence number is never lowered, except by a Logon carrying ResetSeqNumFlag=Y; "
            + "SequenceReset may only increase it.",
            (b, a) => a.Expect < b.Expect && !a.WasReset)
        .Never("OUTBOUND-MONOTONIC",
            "The outbound sequence number is never lowered, except by a Logon carrying ResetSeqNumFlag=Y.",
            (b, a) => a.Next < b.Next && !a.WasReset)
        .Rule("SEQRESET-LOW-REJECTED",
            "SequenceReset with NewSeqNo not greater than the expected sequence number must be rejected with "
            + "SessionRejectReason \"value is incorrect\", and must not lower the sequence number.",
            when: (b, a) => b.Up && a.Got(In.SeqReset, Seq.TooLow),
            then: (b, a) => a.Put(Out.Reject) && a.Expect == b.Expect)

        // ── the outbound sequence number, and the session outliving the connection ───────────────────────────
        .Rule("OUTBOUND-ADVANCES",
            "Each message sent takes the next outbound sequence number, one number per message. A gap on this side "
            + "breaks the counterparty's recovery exactly as badly as a gap on theirs.",
            when: (_, a) => !a.WasReset,
            then: (b, a) => a.Next == Math.Min(b.Next + BitOperations.PopCount((uint)a.Sent), Cap))
        .Rule("SEQNUM-PERSISTS",
            "Sequence numbers belong to the session and not to the connection, so they are not reset when the "
            + "connection is dropped and re established. Only a Logon carrying ResetSeqNumFlag=Y resets them.",
            on: "Reconnect",
            then: (b, a) => a.Expect == b.Expect && a.Next == b.Next)
        .Rule("RESET-RESETS-BOTH",
            "A Logon carrying ResetSeqNumFlag=Y resets the sequence numbers in both directions to 1. Resetting only "
            + "the inbound side leaves the counterparty expecting a number this side will never send. Both are 1 "
            + "after the reset and 2 after the Logon exchange that carried it, one each way.",
            when: (_, a) => a.WasReset,
            then: (_, a) => a.Expect == 2 && a.Next == 2)

        // ── administrative message replies ──────────────────────────────────────────────────────────────────
        .Rule("TESTREQ-ANSWERED",
            "When a TestRequest is received, respond with a Heartbeat containing the TestReqID that was sent.",
            when: (b, a) => b.Up && a.Got(In.TestRequest, Seq.Expected),
            then: (_, a) => a.Put(Out.Heartbeat))
        .Rule("RESEND-ANSWERED",
            "When a ResendRequest is received, resend the requested range, replacing administrative messages with "
            + "a SequenceReset-GapFill.",
            when: (b, a) => b.Up && a.Got(In.ResendRequest, Seq.Expected),
            then: (_, a) => a.Put(Out.Resend))

        // ── logout and termination ──────────────────────────────────────────────────────────────────────────
        .Rule("LOGOUT-REPLY",
            "Upon receipt of a Logout the session responds with a Logout and terminates the connection.",
            when: (b, a) => b.Status == ConnectionStatus.LoggedOn && a.Got(In.Logout),
            then: (_, a) => a.Put(Out.Logout) && a.Status == ConnectionStatus.Disconnected)
        .Never("DISCONNECTED-SILENT",
            "No message is sent on a terminated connection.",
            (b, a) => b.Status == ConnectionStatus.Disconnected && a.Sent != Out.None)
        // FIX says "a reasonable period"; the bound below is this engine's choice of what that means, and pinning it
        // here is what stops a later change to the timing logic from quietly leaving sessions stuck in LogoutSent.
        .Response("LOGOUT-COMPLETES",
            "The initiator of a Logout waits for the confirming Logout before terminating the connection. If it "
            + "does not arrive within a reasonable period the connection is terminated anyway.",
            trigger: (b, a) => a.Status == ConnectionStatus.LogoutSent && b.Status != ConnectionStatus.LogoutSent,
            response: (_, a) => a.Status == ConnectionStatus.Disconnected,
            within: Interval + 1, per: "Tick")

        // ── heartbeats ──────────────────────────────────────────────────────────────────────────────────────
        .Rule("HB-KEEPALIVE",
            "If no data has been sent during the previous HeartBtInt a Heartbeat must be sent, so the counterparty "
            + "can tell the session is alive.",
            on: "Tick",
            when: (b, _) => b.Status == ConnectionStatus.LoggedOn && b.Idle >= Interval,
            then: (_, a) => a.Sent != Out.None || a.Status == ConnectionStatus.Disconnected)
        .Rule("TESTREQ-ON-QUIET",
            "If no data has been received during the previous HeartBtInt plus a reasonable transmission time, a "
            + "TestRequest must be sent to force a Heartbeat from the counterparty.",
            on: "Tick",
            when: (b, _) => b.Status == ConnectionStatus.LoggedOn && b.Quiet >= Interval && !b.TestSent,
            then: (_, a) => a.Put(Out.TestRequest))
        .Response("TESTREQ-TIMEOUT",
            "If a Heartbeat is not received in response to the TestRequest the connection is terminated.",
            trigger: (b, a) => a.TestSent && !b.TestSent,
            response: (_, a) => a.Status == ConnectionStatus.Disconnected,
            within: Interval, cancel: (_, a) => !a.TestSent, per: "Tick")

        // ── deliberate defects, to check the requirements above are strong enough ────────────────────────────
        .Fault("too low is not fatal",
            (b, a) => b.Up && a.RecvSeq == Seq.TooLow
                   && a.Recv is not In.Logout and not In.SeqReset and not In.Logon and not In.LogonReset,
            (b, a) => b with { Recv = a.Recv, RecvSeq = a.RecvSeq, Sent = Out.None, Quiet = 0, TestSent = false })
        .Fault("bad OrigSendingTime ignored instead of rejected",
            (b, a) => b.Up && a.RecvSeq == Seq.DupBadOrig,
            (_, a) => a with { Sent = Out.None })
        // The hole this closes: SEQ-TOO-LOW-FATAL is gated on b.Up, and in AwaitingLogon it is not, so before
        // LOGON-TOO-LOW existed nothing at all covered a too low Logon and the fault below went uncaught.
        .Fault("logon too low is not fatal",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Got(In.Logon, Seq.TooLow),
            (b, a) => b with { Recv = a.Recv, RecvSeq = a.RecvSeq, Sent = Out.None })
        .Fault("outbound seqnum not advanced when sending",
            (_, a) => a.Sent != Out.None,
            (b, a) => a with { Next = b.Next })
        // Two faults for the reconnect, because they are caught from opposite sides: resetting lowers the numbers so
        // monotonicity has it, and only SEQNUM-PERSISTS covers a reconnect that disturbs them without lowering them.
        .Fault("sequence numbers reset on reconnect",
            (b, a) => b.Status == ConnectionStatus.Disconnected && a.Status == ConnectionStatus.AwaitingLogon,
            (_, a) => a with { Expect = 1, Next = 1 })
        .Fault("sequence numbers drift on reconnect",
            (b, a) => b.Status == ConnectionStatus.Disconnected && a.Status == ConnectionStatus.AwaitingLogon,
            (b, a) => a with { Expect = Math.Min(b.Expect + 1, Cap) })
        // Only falsifiable because a reconnect can carry the counters above 1. Without that this fault produces
        // exactly the correct state, since a reset arriving on a fresh connection has nothing to reset.
        .Fault("reset only resets the inbound side",
            (_, a) => a.WasReset,
            (b, a) => a with { Next = Math.Min(b.Next + 1, Cap) })
        .Fault("garbled consumes a seqnum",
            (_, a) => a.Got(In.Garbled),
            (_, a) => a with { Expect = Math.Min(a.Expect + 1, Cap) })
        .Fault("SequenceReset lowers seqnum",
            (_, a) => a.Got(In.SeqReset, Seq.TooLow),
            (_, a) => a with { Expect = 1, Sent = Out.None })
        .Fault("resends on every gap message",
            (b, a) => b.GapOpen && a.RecvSeq == Seq.TooHigh,
            (_, a) => a with { Sent = a.Sent | Out.ResendRequest })
        // Next has to be wound back with Sent, or the fault is "sent nothing but burned a sequence number" and
        // OUTBOUND-ADVANCES catches it first - which passes Faults while leaving HB-KEEPALIVE unproven.
        .Fault("no heartbeat when idle",
            (b, a) => b.Status == ConnectionStatus.LoggedOn && a.Recv == In.Nothing && a.Put(Out.Heartbeat),
            (b, a) => a with { Sent = Out.None, Next = b.Next, Idle = Math.Min(b.Idle + 1, Cap) })
        .Fault("logout never completes",
            (b, a) => b.Status == ConnectionStatus.LogoutSent && a.Status == ConnectionStatus.Disconnected && a.Recv == In.Nothing,
            (_, a) => a with { Status = ConnectionStatus.LogoutSent })
        .Fault("app accepted before logon",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Got(In.App, Seq.Expected),
            (b, a) => a with { Status = ConnectionStatus.AwaitingLogon, Expect = Math.Min(b.Expect + 1, Cap) })
        .Fault("app sent before logon",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Recv == In.Nothing,
            (_, a) => a with { Sent = Out.App })
        // The one the Precedes cannot catch. Expect above 1 is how this says "the first connection did log on" with
        // only a pair of states to work from: nothing advances it but an accepted message, and none is accepted
        // before a Logon. Without that clause the first connection can drop before logging on, no Logon was ever
        // sent, and the Precedes catches it after all - which is what happened on the first attempt.
        .Fault("app sent before the second logon",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && b.Reconnected && b.Expect > 1 && a.Recv == In.Nothing,
            (_, a) => a with { Sent = Out.App })
        .Fault("reject sent before logon",
            (b, a) => b.Status == ConnectionStatus.AwaitingLogon && a.Got(In.Garbled),
            (_, a) => a with { Sent = Out.Reject })
        // Undo only the termination, keeping everything the real transition set. Rebuilding the state from the
        // before-state instead let the fault fabricate a state the model cannot reach, and it was then caught by the
        // wrong requirement - which is what the Caught by column is for.
        .Fault("test request never times out",
            (b, a) => b.TestSent && b.Status == ConnectionStatus.LoggedOn && a.Status == ConnectionStatus.Disconnected && a.Recv == In.Nothing,
            (b, a) => a with { Status = ConnectionStatus.LoggedOn, TestSent = true, Idle = b.Idle, Quiet = b.Quiet });
}
