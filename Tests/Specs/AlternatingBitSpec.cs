namespace Tests.Specs;

using CsCheck;

/// <summary>The Alternating Bit Protocol: how to get reliable, in-order, exactly-once delivery over a channel that
/// loses, duplicates and possibly reorders, using one bit of sequence number. Bartlett, Scantlebury and Wilkinson,
/// 1969, and the standard first example in every protocol verification course since.
/// <para>This is the example that <c>AtMost</c> exists for, and the reason is worth stating because the other four worked
/// examples could not use it. The property is <b>at-most-once delivery</b>: a frame handed to the application once,
/// never twice, however many copies of it the channel makes. Counting deliveries per frame is not something the
/// protocol does - a real receiver keeps one bit, not a tally - so putting the count in the model would be adding
/// bookkeeping that no implementation has, purely to state the requirement. <c>AtMost</c> puts it in the <em>search</em>
/// state instead, which is exactly the distinction its documentation draws: without that, a state reached once and the
/// same state reached for the second time would be one search node and the duplicate would go unreported. The
/// per-element overload gives each frame its own count, because one shared count would let a duplicate of frame 0
/// spend frame 1's budget.</para>
/// <para>It is also checkable against two textbook results rather than against one file, which is a stronger thing to check
/// against. <b>One bit is necessary</b>: a receiver that does not check the bit delivers duplicates. <b>And one bit is
/// sufficient only if the channel preserves order</b>: over a channel that may reorder, one bit is not enough and the
/// protocol fails. Both are configurations here, and both come out as predicted.</para></summary>
public static class AlternatingBitSpec
{
    /// <summary>How many frames the sender has to deliver. Three is enough for the bit to alternate twice, which is what
    /// it takes for a stale duplicate to be mistaken for a fresh frame.</summary>
    public const int Frames = 3;

    /// <summary>Whether the receiver checks the sequence bit. <c>None</c> is the strawman the bit exists to rule out.</summary>
    public enum Seq { None, OneBit }

    /// <summary>Whether the channel may deliver messages out of order. FIFO is the assumption the protocol is proved
    /// under; <c>Reorder</c> is what breaks it.</summary>
    public enum Order { Fifo, Reorder }

    /// <summary>Two channels of two slots each, the sender's and receiver's bits, and what the last step observably did.
    /// A data slot holds <c>1 + bit * Frames + frame</c> and zero for empty; an ack slot holds <c>1 + bit</c>.
    /// <c>JustSent</c> and <c>JustDelivered</c> are how the requirements see events rather than states; they are -1 when
    /// the step did neither.</summary>
    public readonly record struct State(
        int NextFrame, bool SenderBit, bool ExpectedBit, int Delivered,
        int D0, int D1, int A0, int A1, int JustSent, int JustDelivered, bool Lost, bool Duped)
    {
        public static readonly State Start = new(0, false, false, 0, 0, 0, 0, 0, -1, -1, false, false);

        /// <summary>Clears the observation fields, so a step that neither sends nor delivers says so.</summary>
        public State Step() => this with { JustSent = -1, JustDelivered = -1 };
    }

    public static Spec<State> Create(Seq seq = Seq.OneBit, Order order = Order.Fifo)
    {
        var picks = order == Order.Reorder ? Both : Head;
        return Spec.From(State.Start)
            .Print(Show)

            // The sender transmits its current frame tagged with its current bit, and keeps doing so until an
            // acknowledgement moves it on. It never advances on its own, which is what STOP-AND-WAIT below proves.
            .Action("Send", s => s.NextFrame < Frames && s.D1 == 0,
                             s => Push(s.Step(), Data(s.SenderBit, s.NextFrame)) with { JustSent = s.NextFrame })
            .Action("LoseData", s => s.D0 != 0, s => Pop(s.Step(), 0) with { Lost = true })
            .Action("DupData", s => s.D0 != 0 && s.D1 == 0, s => Push(s.Step(), s.D0) with { Duped = true })
            .Action("RecvData", picks, (s, i) => Slot(s, i) != 0, (s, i) => Receive(s, i, seq))

            .Action("LoseAck", s => s.A0 != 0, s => PopAck(s.Step(), 0) with { Lost = true })
            .Action("DupAck", s => s.A0 != 0 && s.A1 == 0, s => PushAck(s.Step(), s.A0) with { Duped = true })
            .Action("RecvAck", picks, (s, i) => AckSlot(s, i) != 0, (s, i) => ReceiveAck(s, i))

            // The requirement this example is here for. One count per frame, in the search state rather than the model.
            .AtMost("DELIVERED-ONCE", "A frame is delivered to the application at most once, however many copies of it "
                + "the channel produces.", 1, All, (_, a, f) => a.JustDelivered == f)

            // A frame cannot arrive before it was sent. Cheap, and it is the requirement that would catch a model where
            // the receiver invented data rather than one where the protocol was wrong.
            .Precedes("NOT-BEFORE-SENT", "A frame is delivered only if it was sent.", All,
                (_, a, f) => a.JustSent == f, (_, a, f) => a.JustDelivered == f)

            // Stop-and-wait, stated the way the protocol document states it: between putting a frame on the wire and its
            // acknowledgement coming back, nothing else goes on the wire. The scope opens on an event and closes on an
            // event, which is what until is for - though the sender's own NextFrame makes it equally expressible as a
            // plain Never, and the docs would tell you to prefer that.
            .NeverAfter("STOP-AND-WAIT", "The sender does not transmit the next frame until the current one has been "
                + "acknowledged.", All,
                after: (_, a, f) => a.JustSent == f,
                never: (_, a, f) => a.JustSent >= 0 && a.JustSent != f,
                until: (_, a, f) => a.NextFrame > f)

            // In order, and never more than were sent. The first is what makes "at most once" worth having: a protocol
            // could deliver each frame once and still deliver them backwards.
            .Never("IN-ORDER", "Frames are delivered in the order they were sent.",
                (b, a) => a.JustDelivered >= 0 && a.JustDelivered != b.Delivered)
            .Invariant("NO-EXTRA", "No more frames are delivered than were sent.", s => s.Delivered <= Frames)

            // Vacuity guards. A run in which the channel behaved perfectly would satisfy everything above and prove
            // nothing about a protocol whose entire purpose is coping with a channel that does not.
            .Reachable("CAN-LOSE", "The channel can lose a message.", s => s.Lost)
            .Reachable("CAN-DUPLICATE", "The channel can duplicate a message.", s => s.Duped)
            .Reachable("CAN-COMPLETE", "Every frame can be delivered.", s => s.Delivered == Frames)
            .Terminal(s => s.NextFrame == Frames && s.Delivered == Frames && s.D0 == 0 && s.A0 == 0);
    }

    static readonly int[] All = [.. Enumerable.Range(0, Frames)];
    static readonly int[] Head = [0];
    static readonly int[] Both = [0, 1];

    static int Data(bool bit, int frame) => 1 + (bit ? Frames : 0) + frame;
    static bool BitOf(int slot) => (slot - 1) >= Frames;
    static int FrameOf(int slot) => (slot - 1) % Frames;

    static int Slot(State s, int i) => i == 0 ? s.D0 : s.D1;
    static int AckSlot(State s, int i) => i == 0 ? s.A0 : s.A1;

    static State Push(State s, int v) => s.D0 == 0 ? s with { D0 = v } : s with { D1 = v };
    static State Pop(State s, int i) => i == 0 ? s with { D0 = s.D1, D1 = 0 } : s with { D1 = 0 };
    static State PushAck(State s, int v) => s.A0 == 0 ? s with { A0 = v } : s with { A1 = v };
    static State PopAck(State s, int i) => i == 0 ? s with { A0 = s.A1, A1 = 0 } : s with { A1 = 0 };

    /// <summary>The receiver takes a frame off the wire. With one bit it delivers only when the bit is the one it is
    /// waiting for and then flips; without one it delivers whatever arrives, which is the strawman. Either way it
    /// acknowledges, because a lost acknowledgement must not wedge the sender.</summary>
    static State Receive(State s, int i, Seq seq)
    {
        var slot = Slot(s, i);
        var bit = BitOf(slot);
        var frame = FrameOf(slot);
        var accept = seq == Seq.None || bit == s.ExpectedBit;
        var next = Pop(s.Step(), i);
        if (accept)
            next = next with { JustDelivered = frame, Delivered = s.Delivered + 1, ExpectedBit = !s.ExpectedBit };
        // Acknowledge the bit just accepted, or re-acknowledge the last one accepted when this was a duplicate.
        var ackBit = accept ? bit : !s.ExpectedBit;
        return next.A1 == 0 ? PushAck(next, 1 + (ackBit ? 1 : 0)) : next;
    }

    /// <summary>The sender takes an acknowledgement off the wire, and moves on only if it is for the frame in hand.</summary>
    static State ReceiveAck(State s, int i)
    {
        var ackBit = AckSlot(s, i) == 2;
        var next = PopAck(s.Step(), i);
        return ackBit == s.SenderBit
            ? next with { NextFrame = s.NextFrame + 1, SenderBit = !s.SenderBit }
            : next;
    }

    /// <summary>Public so a test can print a counterexample the way the report does, rather than with the record's own
    /// ToString, which shows the channel slots as the integers they are encoded as.</summary>
    public static string Show(State s)
    {
        var sb = new System.Text.StringBuilder("snd f").Append(s.NextFrame).Append(s.SenderBit ? "/1" : "/0")
            .Append(" data[");
        foreach (var v in new[] { s.D0, s.D1 })
            if (v != 0) sb.Append('f').Append(FrameOf(v)).Append(BitOf(v) ? "/1 " : "/0 ");
        sb.Append("] ack[");
        foreach (var v in new[] { s.A0, s.A1 }) if (v != 0) sb.Append(v == 2 ? "1 " : "0 ");
        sb.Append("] rcv want").Append(s.ExpectedBit ? "1" : "0").Append(" got=").Append(s.Delivered);
        if (s.JustDelivered >= 0) sb.Append(" >>deliver f").Append(s.JustDelivered);
        return sb.ToString();
    }
}
