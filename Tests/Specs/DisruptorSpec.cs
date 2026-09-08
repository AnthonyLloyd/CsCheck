namespace Tests.Specs;

using CsCheck;

/// <summary>The LMAX Disruptor: a ring buffer that lets several producer threads and several consumer threads hand work
/// between them without a lock, by having each claim a monotonically increasing sequence number and derive its slot from
/// it. The specification is <c>Disruptor_MPMC</c> from the TLA+ examples repository, which models a Rust
/// implementation, and the property is the one it exists to check - that no producer ever writes a slot while a consumer
/// is reading it.
///
/// It earns its place here for a reason none of the other examples can: <b>its state space is genuinely infinite, and no
/// abstraction makes it finite</b>. The sequence counter only ever goes up. The original is in the same position and
/// deals with it the same way, by supplying a bound from outside the module, which is what <c>Boundary</c> is. What
/// closure means here is therefore weaker than elsewhere and worth saying out loud: <em>no data race is reachable
/// within the first N sequences</em>. <c>DisruptorTests</c> raises N and shows the answer stops changing, which is
/// evidence the bound hides nothing rather than a proof that it does not.
///
/// Two things the original carries that this does not, both dropped after checking nothing reads them.
///
/// The ring buffer's per-slot <c>readers</c> and <c>writers</c> sets, which exist to state <c>NoDataRaces</c>, are a
/// function of the thread state: a writer occupies the slot of its claimed sequence exactly while its program counter
/// says <c>Access</c>, and a reader occupies the slot of its next sequence on the same condition. So the invariant can
/// be computed from the counters, and the sets are not part of the state at all.
///
/// The slot <em>values</em> and the <c>consumed</c> history are only ever appended to or read into that history, which
/// the original itself labels as being for liveness. Nothing any safety requirement asks depends on them.
///
/// The one piece of cleverness kept verbatim is how publication is encoded. Rather than remembering which sequence a
/// slot holds, one bit per slot flips on each publish, and a sequence counts as published when the bit matches the
/// parity of its round. The original's comment explains why that is sound: producers cannot overtake consumers.</summary>
public static class DisruptorSpec
{
    /// <summary>Two of each, which is the smallest configuration that can race: one producer and one consumer cannot
    /// contend for a slot, and two of a kind are needed for a claim to be overtaken.</summary>
    public const int Writers = 2;
    public const int Readers = 2;

    /// <summary>Writers are threads 0 and 1, readers 2 and 3. A bit set in <c>Pc</c> means that thread is inside a slot
    /// (the original's <c>Access</c>); clear means it is between slots (<c>Advance</c>).</summary>
    public readonly record struct State(int Next, int ClaimedA, int ClaimedB, int CursorA, int CursorB, int Published, int Pc)
    {
        public int Claimed(int w) => w == 0 ? ClaimedA : ClaimedB;
        public State WithClaimed(int w, int v) => w == 0 ? this with { ClaimedA = v } : this with { ClaimedB = v };
        public int Cursor(int r) => r == 0 ? CursorA : CursorB;
        public State WithCursor(int r, int v) => r == 0 ? this with { CursorA = v } : this with { CursorB = v };
        public bool InSlot(int thread) => (Pc & (1 << thread)) != 0;
        public State Enter(int thread) => this with { Pc = Pc | (1 << thread) };
        public State Leave(int thread) => this with { Pc = Pc & ~(1 << thread) };
        public int MinCursor => Math.Min(CursorA, CursorB);
    }

    /// <summary>The specification for a ring of <paramref name="size"/> slots, explored over the first
    /// <paramref name="sequences"/> claims.</summary>
    public static Spec<State> Create(int size, int sequences) => CreateWithGate(size, sequences, slack: 0);

    /// <summary>The same with the gate loosened by <paramref name="slack"/>, so a producer may claim a slot that many
    /// sequences earlier than it should. Zero is the design; anything more is the off by one that breaks it.</summary>
    public static Spec<State> CreateWithGate(int size, int sequences, int slack)
    {
        var start = new State(0, -1, -1, -1, -1, 0, 0);
        return Spec.From(start)
            .Print(s => Show(s, size))
            // The gate: a producer may only claim a sequence once the slowest consumer is at most a full cycle behind,
            // which is the whole of what keeps a slot from being written while it is still being read.
            .Action("BeginWrite", Two, (s, w) => !s.InSlot(w) && s.MinCursor >= s.Next - size - slack,
                                       (s, w) => s.WithClaimed(w, s.Next).Enter(w) with { Next = s.Next + 1 })
            .Action("EndWrite", Two, (s, w) => s.InSlot(w),
                                     (s, w) => s.Leave(w) with { Published = s.Published ^ (1 << Index(s.Claimed(w), size)) })
            .Action("BeginRead", Two, (s, r) => !s.InSlot(Readers0 + r) && IsPublished(s, s.Cursor(r) + 1, size),
                                      (s, r) => s.Enter(Readers0 + r))
            .Action("EndRead", Two, (s, r) => s.InSlot(Readers0 + r),
                                    (s, r) => s.Leave(Readers0 + r).WithCursor(r, s.Cursor(r) + 1))
            // The reason the specification exists. Both conjuncts of the original's NoDataRaces: no slot holds a reader
            // and a writer at once, and no slot holds two writers.
            .Invariant("NO-DATA-RACES",
                "Read and write accesses to each slot are tracked to detect data races. All models using the RingBuffer "
                + "should assert the NoDataRaces invariant.",
                s => NoDataRaces(s, size))
            // TypeOk, reduced to the parts that are not already true by construction of the C# types. Deliberately only
            // ranges, as the original is: the window bound that the gate implies is a consequence of the design rather
            // than a type, and putting it here would have it caught before the race it is there to prevent.
            .Invariant("TYPE-OK", "TLA+ is untyped, thus lets verify the range of some values in each state.",
                s => s.Next >= 0 && s.ClaimedA >= -1 && s.ClaimedB >= -1 && s.CursorA >= -1 && s.CursorB >= -1)
            // Without these the proof could hold because nothing interesting happened. The third is the one that
            // matters: a ring buffer that never wraps has not been tested as a ring buffer.
            .Reachable("CAN-CLAIM-ALL", "Every slot can be claimed at once.",
                s => Occupancy(s, size) == size)
            .Reachable("CAN-CONTEND", "Two threads can be inside the ring at the same time.",
                s => System.Numerics.BitOperations.PopCount((uint)s.Pc) >= 2)
            .Reachable("CAN-WRAP", "A sequence can be claimed for a slot that has already been used.",
                s => s.Next > size)
            .Boundary(s => s.Next <= sequences);
    }

    /// <summary>Three ways an implementation of this could be wrong, for <c>Faults</c> to inject one at a time. Each is
    /// a mistake someone could plausibly make in the Rust or the Java, not an arbitrary corruption: publish before the
    /// write has finished, keep hold of a slot after publishing it, and advance a read cursor past a slot that was never
    /// consumed. All three should be caught by the invariant the specification exists for, and a fault that is not is a
    /// requirement that is missing.</summary>
    public static Spec<State> CreateWithFaults(int size, int sequences)
        => Create(size, sequences)
        .Fault("PublishBeforeWriting",
            // A writer has just entered a slot, and the slot is marked published in the same breath.
            (b, a) => Entered(b, a, 0) || Entered(b, a, 1),
            (b, a) => a with { Published = a.Published ^ (1 << Index(a.Claimed(Entered(b, a, 0) ? 0 : 1), size)) })
        .Fault("HoldsSlotAfterPublishing",
            // EndWrite published the slot but the writer never left it.
            (b, a) => Left(b, a, 0) || Left(b, a, 1),
            (b, a) => a.Enter(Left(b, a, 0) ? 0 : 1))
        .Fault("CursorRunsAhead",
            // EndRead advanced the cursor two slots instead of one, so the gate believes more has been consumed.
            (b, a) => a.CursorA != b.CursorA || a.CursorB != b.CursorB,
            (b, a) => a.CursorA != b.CursorA ? a.WithCursor(0, a.CursorA + 1) : a.WithCursor(1, a.CursorB + 1));

    static bool Entered(State b, State a, int w) => !b.InSlot(w) && a.InSlot(w);
    static bool Left(State b, State a, int w) => b.InSlot(w) && !a.InSlot(w);

    const int Readers0 = 2;
    static readonly int[] Two = [0, 1];

    /// <summary>The slot a sequence maps to, which is the original's <c>IndexOf</c>.</summary>
    static int Index(int sequence, int size) => sequence % size;

    /// <summary>The original's publication encoding, kept verbatim: one bit per slot, flipped on every publish, and a
    /// sequence is published when that bit agrees with whether its round number is even.</summary>
    static bool IsPublished(State s, int sequence, int size)
        => ((s.Published >> Index(sequence, size)) & 1) == (sequence / size % 2 == 0 ? 1 : 0);

    /// <summary>Which threads occupy which slot, derived rather than stored. A writer is in the slot of the sequence it
    /// claimed, a reader in the slot of the sequence it is about to consume, and both only while inside.</summary>
    static bool NoDataRaces(State s, int size)
    {
        for (int slot = 0; slot < size; slot++)
        {
            var writers = 0;
            for (int w = 0; w < Writers; w++)
                if (s.InSlot(w) && Index(s.Claimed(w), size) == slot) writers++;
            if (writers > 1) return false;
            if (writers == 0) continue;
            for (int r = 0; r < Readers; r++)
                if (s.InSlot(Readers0 + r) && Index(s.Cursor(r) + 1, size) == slot) return false;
        }
        return true;
    }

    /// <summary>How many slots are occupied by anyone, for the coverage requirement.</summary>
    static int Occupancy(State s, int size)
    {
        var used = 0;
        for (int w = 0; w < Writers; w++) if (s.InSlot(w)) used |= 1 << Index(s.Claimed(w), size);
        for (int r = 0; r < Readers; r++) if (s.InSlot(Readers0 + r)) used |= 1 << Index(s.Cursor(r) + 1, size);
        return System.Numerics.BitOperations.PopCount((uint)used);
    }

    /// <summary>Public so a test can print a counterexample the way the report does, rather than as the raw encoding.</summary>
    public static string Show(State s, int size)
    {
        var sb = new System.Text.StringBuilder("next=").Append(s.Next).Append(" claimed=[");
        for (int w = 0; w < Writers; w++) sb.Append(w == 0 ? "" : ",").Append(s.Claimed(w)).Append(s.InSlot(w) ? "*" : "");
        sb.Append("] read=[");
        for (int r = 0; r < Readers; r++)
            sb.Append(r == 0 ? "" : ",").Append(s.Cursor(r)).Append(s.InSlot(Readers0 + r) ? "*" : "");
        sb.Append("] published=");
        for (int i = 0; i < size; i++) sb.Append((s.Published >> i) & 1);
        return sb.ToString();
    }
}
