namespace Tests.Specs;

using CsCheck;

/// <summary>A distributed lease and the resource it is supposed to protect, specified three ways. Two of the three
/// do not satisfy their own safety requirement, so this example is the tool being used to choose a design rather
/// than to find a bug in one.
/// <para>The argument is Martin Kleppmann's (How to do distributed locking, 2016). A lease has to expire or a crashed
/// client holds the lock forever, but nothing bounds the delay between a client checking that it holds the lease and
/// its write actually landing - a GC pause, a page fault, a stalled network. So the lock service and the client can
/// disagree about who holds the lease, and the property that matters is not "one client holds the lock record" but
/// "one client is mutating the resource".</para>
/// <para>Three modelling choices worth reading before the code:</para>
/// <para>1. The client's pause is not an action. It is the gap between <c>Read</c> and <c>Write</c>, which are separate
///    actions with anything at all allowed in between. Nothing needs to say how long a pause may be.
/// 2. Lease expiry is nondeterministic rather than clocked. The property does not depend on how long a lease lasts,
///    so a clock would only add a state dimension.
/// 3. Tokens are bounded rather than saturating, and <c>Acquire</c> is disabled at the bound. The whole point of a
///    fencing token is its ordering, and a saturating counter would hand out the same token twice and manufacture a
///    counterexample against the abstraction. Ages and timers can saturate because only their comparison to a
///    threshold matters; anything whose ordering carries the property cannot.</para></summary>
public static class FencingSpec
{
    /// <summary>How many lease acquisitions to explore. Two is enough to show the lost update; three leaves room for
    /// a superseded holder to come back on a new lease.</summary>
    public const int Grants = 3;

    /// <summary>Which accesses the resource checks the fencing token on. The three configurations of the same system.</summary>
    public enum Fence
    {
        /// <summary>No token at all: a plain lease, the way it is usually written.</summary>
        None,
        /// <summary>The token is checked when writing, which is what "add fencing tokens" is normally taken to mean.</summary>
        Writes,
        /// <summary>The token is presented and checked on every access, reads included.</summary>
        Every,
    }

    public enum Client { None, One, Two }
    public enum NodePhase { Idle, Holding }
    public enum Last { Nothing, ReadOk, ReadRefused, WriteOk, WriteRefused }

    /// <summary>What a client holds in hand. <c>Unread</c> matters: a client that never read has nothing to lose, so
    /// a write from it is not a lost update however out of date its lease is. A plain stale flag conflated the two and
    /// produced a counterexample against a design that was fine.</summary>
    public enum Held { Unread, Current, Stale }

    /// <summary>One client, with the token it believes it holds and what it read on that belief.</summary>
    public readonly record struct Node(NodePhase Phase, int Token, Held Held);

    public readonly record struct State(Client Holder, int Issued, Node One, Node Two, int Fenced,
        Client Actor, int ActorToken, Last Last)
    {
        public static readonly State Start = new(Client.None, 0, default, default, 0, Client.None, 0, Last.Nothing);

        public Node Of(Client c) => c == Client.One ? One : Two;
        State With(Client c, Node n) => c == Client.One ? this with { One = n } : this with { Two = n };
        static Client Other(Client c) => c == Client.One ? Client.Two : Client.One;
        State Step(Client c) => this with { Actor = c, ActorToken = 0, Last = Last.Nothing };

        /// <summary>The lock service grants the lease and mints the next token.</summary>
        public State Acquire(Client c)
            => (Step(c) with { Holder = c, Issued = Issued + 1 }).With(c, new Node(NodePhase.Holding, Issued + 1, Held.Unread));

        /// <summary>The lease times out at the lock service. The client is not told, and goes on believing it holds
        /// the lease - which is the entire problem.</summary>
        public State Expire() => Step(Client.None) with { Holder = Client.None };

        /// <summary>The client finishes and releases. If the lease already expired the release is a no-op at the
        /// service, which is why this is not guarded on still being the holder.</summary>
        public State Done(Client c)
            => (Step(c) with { Holder = Holder == c ? Client.None : Holder }).With(c, default);

        /// <summary>The client reads the resource, so what it is about to write is based on what is there now.</summary>
        public State Read(Client c, Fence fence)
        {
            var node = Of(c);
            var s = Step(c) with { ActorToken = node.Token };
            if (fence == Fence.Every && node.Token < Fenced) return s with { Last = Last.ReadRefused };
            return (s with { Fenced = Honoured(node.Token, fence == Fence.Every), Last = Last.ReadOk })
                .With(c, node with { Held = Held.Current });
        }

        /// <summary>The client writes, believing it holds the lease. The resource refuses a token below the highest it
        /// has already honoured; with no fencing at all it has no idea and takes the write.</summary>
        public State Write(Client c, Fence fence)
        {
            var node = Of(c);
            var s = Step(c) with { ActorToken = node.Token };
            if (fence != Fence.None && node.Token < Fenced) return s with { Last = Last.WriteRefused };
            var other = Of(Other(c));
            return (s with { Fenced = Honoured(node.Token, fence != Fence.None), Last = Last.WriteOk })
                .With(Other(c), other with { Held = other.Held == Held.Current ? Held.Stale : other.Held });
        }

        int Honoured(int token, bool tracked) => tracked && token > Fenced ? token : Fenced;

        public override string ToString()
            => string.Concat(
                "lease=", Holder == Client.None ? "free" : Holder.ToString(), "/", Issued.ToString(),
                " fenced=", Fenced.ToString(),
                " 1", Show(One), " 2", Show(Two),
                Last == Last.Nothing ? "" : string.Concat("  ", Actor.ToString(), " t", ActorToken.ToString(),
                    " ", Last.ToString()));

        static string Show(Node n)
            => n.Phase == NodePhase.Idle ? "[idle]" : $"[t{n.Token}{(n.Held == Held.Unread ? "]" : n.Held == Held.Current ? " read]" : " stale]")}";
    }

    static readonly Client[] Clients = [Client.One, Client.Two];
    // Only tokens that something can supersede. Token 3 is the last one issued, so Fenced > 3 is unreachable and
    // an instance for it would sit in the coverage table reporting NEVER forever.
    static readonly int[] Tokens = [1, 2];

    /// <summary>The same system either way; <paramref name="fence"/> is the design decision under test. One
    /// specification and several configurations is how you compare designs rather than argue about them.</summary>
    public static Spec<State> Create(Fence fence)
        => Spec.From(State.Start)
        .Print(s => s.ToString())

        .Action("Acquire", Clients, (s, c) => s.Holder == Client.None && s.Of(c).Phase == NodePhase.Idle && s.Issued < Grants,
            (s, c) => s.Acquire(c), weight: 4)
        .Action("Read", Clients, (s, c) => s.Of(c).Phase == NodePhase.Holding, (s, c) => s.Read(c, fence), weight: 4)
        .Action("Write", Clients, (s, c) => s.Of(c).Phase == NodePhase.Holding, (s, c) => s.Write(c, fence), weight: 4)
        .Action("Done", Clients, (s, c) => s.Of(c).Phase == NodePhase.Holding, (s, c) => s.Done(c), weight: 2)
        .Action("Expire", s => s.Holder != Client.None, s => s.Expire(), weight: 2)

        // Not a design deadlock: the grant bound is what stops these states, not the protocol. Declaring them keeps
        // the deadlock count meaningful, so a state the design really cannot leave would still show up.
        .Terminal(s => s.Issued == Grants && s.Holder == Client.None
                    && s.One.Phase == NodePhase.Idle && s.Two.Phase == NodePhase.Idle)

        // The lock service is not what is being specified here, so two holders at once is deliberately not
        // representable: Holder is a single value. Choosing whose bugs you are hunting is what keeps a model small.
        .Invariant("TOKEN-ISSUED-BEFORE-HONOURED",
            "The resource never honours a token the lock service has not issued.",
            s => s.Fenced <= s.Issued)
        // Every requirement below is about what happens when one client supersedes another, so if that cannot happen
        // they all pass for the wrong reason. Stated of the lease rather than the fence so it holds in all three
        // configurations, including the one where the resource never tracks a token at all.
        .Reachable("CAN-SUPERSEDE",
            "One client can come to hold the lease after another has held it.",
            s => s.Issued >= 2)

        .Never("NO-LOST-UPDATE",
            "A write is never accepted from a client whose data was overwritten while it was not looking. This is the "
            + "property the lock exists for, and it is about the resource, not about who holds the lock record.",
            (b, a) => a.Last == Last.WriteOk && b.Of(a.Actor).Held == Held.Stale)
        .Never("FENCE-NEVER-RETREATS",
            "The highest token the resource has honoured never decreases.",
            (b, a) => a.Fenced < b.Fenced)
        .Never("LIVE-HOLDER-NEVER-REFUSED",
            "A client that really does still hold the lease is never refused. Without this a resource that rejected "
            + "everything would satisfy the safety requirement perfectly. Faults is what shows this one is live.",
            (b, a) => b.Holder == a.Actor && a.Last is Last.ReadRefused or Last.WriteRefused)

        // One instance per token, not per client. Per client would be wrong: a client that is superseded, releases and
        // acquires again is entitled to write on its new token, and a single per-client history cannot tell the two
        // leases apart. The subject of this requirement is the lease, and the token names it.
        .NeverAfter("SUPERSEDED-TOKEN-REFUSED",
            "Once the resource has honoured a token, no access on a lower token is ever accepted again.",
            Tokens,
            after: (_, a, t) => a.Fenced > t,
            never: (_, a, t) => a.Last is Last.ReadOk or Last.WriteOk && a.ActorToken == t)

        .Fault("resource forgets to record the token",
            (b, a) => a.Fenced > b.Fenced,
            (b, a) => a with { Fenced = b.Fenced })
        .Fault("resource compares tokens with <=",
            (b, a) => a.Last is Last.ReadOk or Last.WriteOk && a.ActorToken == b.Fenced,
            (b, a) => a with { Fenced = b.Fenced, Last = a.Last == Last.ReadOk ? Last.ReadRefused : Last.WriteRefused })
        // A fault for a client reusing its previous token was here. Faults reported it caught by NOTHING, and it was
        // right: a reused token is either below the fence and refused, or equal to it and harmless. The hypothesised
        // bug was not one. The lock service reissuing a token is the real defect, and that is caught.
        .Fault("lock service reissues the same token",
            (b, a) => a.Issued > b.Issued && b.Issued > 0,
            (b, a) => SetToken(a with { Issued = b.Issued }, b.Issued))
        .Fault("only writes are fenced",
            (b, a) => a.Last == Last.ReadOk && a.Fenced > b.Fenced,
            (b, a) => a with { Fenced = b.Fenced });

    static State SetToken(State s, int token)
        => s.Actor == Client.One ? s with { One = s.One with { Token = token } }
                                 : s with { Two = s.Two with { Token = token } };
}
