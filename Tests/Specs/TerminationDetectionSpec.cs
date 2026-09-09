namespace Tests.Specs;

using CsCheck;

/// <summary>Shmuel Safra's algorithm for detecting that a distributed computation on a ring has finished, published by
/// Dijkstra as EWD 998. The specification is <c>EWD998</c> from the TLA+ examples repository.
/// <para>A token walks the ring from node N-1 down to node 0 accumulating each node's message balance, and node 0 declares
/// termination when a white token comes home with the balances cancelling out. The safety property is that a detection
/// is never announced while a message is still in flight.</para>
/// <para>Its counters are unbounded in both directions — a node's counter is sends minus receives — so the original bounds
/// them with a <c>StateConstraint</c>; that is <c>Boundary</c> here. The bound is the original's verbatim.</para>
/// <para>It also carries Safra's inductive invariant, which is the argument for why the safety property holds rather than
/// merely the conclusion. Checking it proves the algorithm rather than just testing it.</para>
/// <para>TLA+ lets the initial state be a set; a Spec starts from one state. So the 192 configurations (any activity, any
/// colouring, any token position) are chosen by three setup actions, one per conjunct of the original's <c>Init</c>.
/// Verified against TLC 1.7.4 on <c>EWD998Small.cfg</c> (N=3): TLC gives 1,520,618 distinct states; this spec gives
/// 1,520,691 — the 73 extra are the setup scaffold states before the first protocol state is reached.</para></summary>
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
            // The original's StateConstraint, verbatim (EWD998.tla, StateConstraint).
            // A counter is sends minus receives so it is unbounded above and below.
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
