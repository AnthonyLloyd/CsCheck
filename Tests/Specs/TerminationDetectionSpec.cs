namespace Tests.Specs;

using CsCheck;

/// <summary>Shmuel Safra's algorithm for detecting that a distributed computation on a ring has finished, published by
/// Dijkstra as EWD 998. The specification is <c>EWD998</c> from the TLA+ examples repository, one of the most worked over
/// specs in that collection.
///
/// The problem: nodes send each other messages and go idle, messages take time to arrive, and no node can see the whole
/// system. A token walks the ring from node N-1 down to node 0 accumulating each node's message balance, and node 0
/// declares termination when a white token comes home with the balances cancelling out. Getting that wrong means
/// announcing termination while a message is still in flight, which is the safety property here.
///
/// Three things make it the right third example.
///
/// It is the largest by two orders of magnitude, and the original publishes its own numbers for the configuration this
/// uses - 1.3 million distinct states and a diameter of 60 for a ring of three - so the size can be checked and not just
/// admired.
///
/// Its counters are unbounded in <em>both</em> directions: a node's counter is messages sent minus messages received, so
/// it goes negative, and the token's accumulator goes negative with it. The original bounds them from outside the module
/// with a <c>StateConstraint</c>, under the comment "Bound the otherwise infinite state space that TLC has to check".
/// That is <c>Boundary</c>, and the bound is the original's own, not one invented here.
///
/// And it carries Safra's <em>inductive</em> invariant, which is a much stronger and more interesting claim than the
/// safety property it implies. The safety property says a false detection never happens; the inductive invariant says
/// why, as a disjunction of four cases about which part of the ring the token has already passed. Checking it is
/// checking the argument rather than the conclusion.
///
/// One difference from the original that has to be accounted for when comparing counts. TLA+ lets the initial state be
/// a <em>set</em> - here every combination of activity and colour, and every token position - while a Spec starts from
/// one state. So the 192 configurations are chosen by three setup actions, one per conjunct of the original's
/// <c>Init</c>: activity, then colour, then the token. The reachable protocol states are exactly the original's; what
/// differs is a little scaffolding in front of them and three steps of depth.
///
/// Three actions rather than one with 192 cases, and the reason is measured. A guard is evaluated once per argument at
/// every state in the space, so a 192 case domain costs 192 guard calls per state even though the action can only ever
/// fire at the root - 48 million calls here, which was 40% of the run - and per argument coverage gives it 192 rows in
/// the report. Split by conjunct it is 19 cases and 19 rows, and it reads closer to the original.</summary>
public static class TerminationDetectionSpec
{
    /// <summary>A ring of three. The original's published numbers are for three and for four, and four is 219 million
    /// distinct states, which at roughly 200 bytes each is beyond any machine this runs on.</summary>
    public const int N = 3;

    /// <summary>Activity and colour as a bit per node, per node message balances, the number of messages in flight to
    /// each node, and the token. <c>Phase</c> is which conjunct of the original's Init is still to be chosen, and
    /// <see cref="Running"/> once they all have been.</summary>
    public readonly record struct State(
        int Active, int Black, int C0, int C1, int C2, int P0, int P1, int P2,
        int Pos, int Q, bool TokenBlack, int Phase)
    {
        /// <summary>Past setup, so the protocol's own actions are enabled and its requirements apply.</summary>
        public bool Running => Phase == 3;

        public bool IsActive(int i) => (Active & (1 << i)) != 0;
        public bool IsBlack(int i) => (Black & (1 << i)) != 0;
        public int Counter(int i) => i == 0 ? C0 : i == 1 ? C1 : C2;
        public int Pending(int i) => i == 0 ? P0 : i == 1 ? P1 : P2;

        public State WithActive(int i, bool on)
            => this with { Active = on ? Active | (1 << i) : Active & ~(1 << i) };
        public State WithBlack(int i, bool on)
            => this with { Black = on ? Black | (1 << i) : Black & ~(1 << i) };
        public State WithCounter(int i, int v)
            => i == 0 ? this with { C0 = v } : i == 1 ? this with { C1 = v } : this with { C2 = v };
        public State WithPending(int i, int v)
            => i == 0 ? this with { P0 = v } : i == 1 ? this with { P1 = v } : this with { P2 = v };

        /// <summary>The original's B: the number of messages on their way.</summary>
        public int InFlight => P0 + P1 + P2;
        public int TotalCounter => C0 + C1 + C2;

        public override string ToString()
        {
            if (!Running) return string.Concat("(setup phase ", Phase.ToString(), ")");
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
                sb.Append(i == 0 ? "" : " ").Append('n').Append(i).Append(IsActive(i) ? "+" : "-")
                  .Append(IsBlack(i) ? "B" : "w").Append(" c=").Append(Counter(i)).Append(" p=").Append(Pending(i));
            return sb.Append(" | token@").Append(Pos).Append(" q=").Append(Q)
                     .Append(TokenBlack ? " black" : " white").ToString();
        }
    }

    /// <summary>The specification, bounded exactly as the original's <c>StateConstraint</c> bounds it.
    /// <paramref name="anyInitialColour"/> chooses between the current original's <c>color \in [Node -&gt; Color]</c> and
    /// the narrower all-white start, which is what the size comparison in the tests turns on.</summary>
    public static Spec<State> Create(int counterMax = 3, int pendingMax = 3, int tokenMax = 9,
        bool anyInitialColour = true)
    {
        return Spec.From(default(State))
            // The original's Init, one action per conjunct: any activity, then any colouring (or all white), then any
            // token position. The token always starts black with a zero accumulator, so the first round can never
            // conclude - Rule 6.
            .Action("SetupActive", Masks, (s, m) => s.Phase == 0, (s, m) => s with { Active = m, Phase = 1 })
            .Action("SetupColour", anyInitialColour ? Masks : White, (s, m) => s.Phase == 1,
                                                                    (s, m) => s with { Black = m, Phase = 2 })
            .Action("SetupToken", Nodes, (s, p) => s.Phase == 2,
                                         (s, p) => s with { Pos = p, TokenBlack = true, Phase = 3 })
            // Rules 1 + 5 + 6. Node 0 starts a fresh round when the last one was not conclusive.
            .Action("InitiateProbe", s => s.Running && s.Pos == 0
                                       && (s.TokenBlack || s.IsBlack(0) || s.C0 + s.Q > 0),
                                     s => s.WithBlack(0, false) with { Pos = N - 1, Q = 0, TokenBlack = false })
            // Rules 2 + 4 + 7. An idle node hands the token on, adding its balance and its blackness to it.
            .Action("PassToken", Inner, (s, i) => s.Running && !s.IsActive(i) && s.Pos == i,
                                        (s, i) => s.WithBlack(i, false) with
                                        {
                                            Pos = i - 1,
                                            Q = s.Q + s.Counter(i),
                                            TokenBlack = s.TokenBlack || s.IsBlack(i),
                                        })
            // Rule 0 for the sender. An active node may send to any other node.
            .Action("SendMsg", Sends, (s, m) => s.Running && s.IsActive(m.From),
                                      (s, m) => s.WithCounter(m.From, s.Counter(m.From) + 1)
                                                 .WithPending(m.To, s.Pending(m.To) + 1))
            // Rules 0 and 3. Receipt decrements the balance, blackens the node and reactivates it.
            .Action("RecvMsg", Nodes, (s, i) => s.Running && s.Pending(i) > 0,
                                      (s, i) => s.WithPending(i, s.Pending(i) - 1)
                                                 .WithCounter(i, s.Counter(i) - 1)
                                                 .WithBlack(i, true).WithActive(i, true))
            .Action("Deactivate", Nodes, (s, i) => s.Running && s.IsActive(i), (s, i) => s.WithActive(i, false))

            // The main safety property: a detection is never wrong.
            .Invariant("TERMINATION-DETECTION",
                "Main safety property: if there is a white token at node 0 and there are no in-flight messages then "
                + "every node is inactive.",
                s => !Detected(s) || Terminated(s))
            // Safra's inductive invariant, which is the argument for why the above holds. Split into its two halves so a
            // failure says which, since the second is a disjunction of four quite different cases.
            .Invariant("SAFRA-P0",
                "The number of counted messages at each node and the number of messages in transit is consistent.",
                s => !s.Running || s.InFlight == s.TotalCounter)
            .Invariant("SAFRA-INV", "Safra's inductive invariant.", s => !s.Running || SafraDisjunction(s))
            // Without these the proof could hold vacuously: a ring that never terminates never risks detecting it, and
            // one that never detects has not tested the detection rule.
            .Reachable("CAN-TERMINATE", "The system can terminate.", Terminated)
            .Reachable("CAN-DETECT", "Termination can be detected.", Detected)
            .Reachable("CAN-FLY", "A message can be in flight.", s => s.InFlight > 0)
            .Reachable("CAN-BLACKEN", "A node can be blackened.", s => s.Black != 0)
            // The original's StateConstraint, verbatim, and the reason this needs one: a counter is sends minus
            // receives, so it is unbounded above and below, and nothing about the algorithm bounds it.
            .Boundary(s => !s.Running
                        || (s.C0 <= counterMax && s.C1 <= counterMax && s.C2 <= counterMax
                         && s.P0 <= pendingMax && s.P1 <= pendingMax && s.P2 <= pendingMax
                         && s.Q <= tokenMax));
    }

    /// <summary>The original's <c>terminationDetected</c>.</summary>
    public static bool Detected(State s)
        => s.Running && s.Pos == 0 && !s.TokenBlack && s.Q + s.C0 == 0 && !s.IsBlack(0) && !s.IsActive(0);

    /// <summary>The original's <c>Termination</c>: nothing running and nothing in flight.</summary>
    public static bool Terminated(State s) => s.Running && s.Active == 0 && s.InFlight == 0;

    /// <summary>The disjunction P1 \/ P2 \/ P3 \/ P4 of Safra's invariant. P1 says the token has already passed a
    /// quiescent suffix whose balances it carries; P2 that the prefix it has not reached still has work outstanding;
    /// P3 that something in that prefix is black; P4 that the token itself is black.</summary>
    static bool SafraDisjunction(State s)
    {
        // P1
        var suffixIdle = true;
        var suffixSum = 0;
        for (int i = s.Pos + 1; i < N; i++)
        {
            if (s.IsActive(i)) suffixIdle = false;
            suffixSum += s.Counter(i);
        }
        if (suffixIdle && (s.Pos == N - 1 ? s.Q == 0 : s.Q == suffixSum)) return true;
        // P2 and P3 over the prefix the token has not yet reached.
        var prefixSum = 0;
        for (int i = 0; i <= s.Pos; i++)
        {
            prefixSum += s.Counter(i);
            if (s.IsBlack(i)) return true;
        }
        return prefixSum + s.Q > 0 || s.TokenBlack;
    }

    static readonly int[] Nodes = [0, 1, 2];
    /// <summary>PassToken is over Node \ {0}: node 0 starts rounds rather than passing the token on.</summary>
    static readonly int[] Inner = [1, 2];

    public readonly record struct Msg(int From, int To)
    {
        public override string ToString() => string.Concat(From.ToString(), "->", To.ToString());
    }

    static readonly Msg[] Sends = [.. from i in Nodes from j in Nodes where i != j select new Msg(i, j)];

    /// <summary>Every subset of the nodes, as a bit per node, for choosing which are active and which are black.</summary>
    static readonly int[] Masks = [.. Enumerable.Range(0, 1 << N)];
    static readonly int[] White = [0];
}
