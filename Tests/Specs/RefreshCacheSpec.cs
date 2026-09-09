namespace Tests.Specs;

using System;
using CsCheck;
using static Tests.Specs.RefreshCache;

/// <summary>A refresh-on-access cache as an executable specification of <see cref="RefreshCache"/>: values are
/// served immediately from the slot, and a read that finds the value stale kicks off a single background load
/// rather than blocking on it.
/// <para>Unlike the FIX session layer there is no document to be faithful to. The quotes below are the design decisions,
/// and writing them down is the point: "serve stale while the loader is down" and "give up after a hard limit" are
/// both defensible, and the specification is where you choose.</para>
/// <para>Concurrency is modelled by making the interleaving points actions. A load is not an atomic step: <c>Read</c>
/// starts it, and a later <c>Complete</c> or <c>Fail</c> ends it, with anything at all allowed in between. Exhaustive
/// exploration then covers every interleaving instead of hoping a thread schedule hits the interesting one.</para></summary>
public static class RefreshCacheSpec
{
    /// <summary>One key's slot. <c>Loads</c> counts loads in flight. It is an int rather than a bool on purpose:
    /// making the two-in-flight state representable is what lets SINGLE-FLIGHT be stated as an invariant and proved
    /// unreachable. A bool would make the bug unrepresentable in the model while leaving it perfectly possible in
    /// the code being specified.</summary>
    public readonly record struct Slot(int Version, int Age, int Loads)
    {
        public bool Present => Version > 0;
        public bool Stale => Age >= Ttl;
        public Slot Loaded() => new(Math.Min(Version + 1, Cap), 0, Loads - 1);
        public Slot Start() => this with { Loads = Loads + 1 };
        public Slot Older() => this with { Age = Math.Min(Age + 1, Cap) };
    }

    public readonly record struct State(Slot A, Slot B, string Touched, Served Served, bool Started)
    {
        public static readonly State Empty = new(default, default, "A", Served.None, false);

        public Slot Of(string k) => k == "A" ? A : B;
        State With(string k, Slot s) => k == "A" ? this with { A = s } : this with { B = s };
        State Step(string k) => this with { Touched = k, Served = Served.None, Started = false };

        /// <summary>A caller asks for a key. Whatever is in the slot is handed straight back; a stale or absent value
        /// additionally kicks off a load, unless one is already in flight for that key.</summary>
        public State Read(string k)
        {
            var slot = Of(k);
            var s = Step(k) with { Served = !slot.Present ? Served.Miss : slot.Stale ? Served.Stale : Served.Fresh };
            return slot.Loads == 0 && (!slot.Present || slot.Stale) ? s.With(k, slot.Start()) with { Started = true } : s;
        }

        /// <summary>A load returned. The slot takes the new value and its age restarts.</summary>
        public State Complete(string k) => Step(k).With(k, Of(k).Loaded());

        /// <summary>A load threw. The previous value is kept and served stale rather than evicted, so a failing
        /// loader degrades availability instead of destroying it.</summary>
        public State Fail(string k) => Step(k).With(k, Of(k) with { Loads = Of(k).Loads - 1 });

        public State Tick() => (this with { Touched = "A", Served = Served.None, Started = false })
            .With("A", A.Older()).With("B", B.Older());

        public override string ToString()
            => string.Concat("A", Show(A), " B", Show(B),
                Served == Served.None ? "" : $"  {Touched}->{Served}",
                Started ? " load!" : "");

        static string Show(Slot s) => $"[v{s.Version} age{s.Age}{(s.Loads == 0 ? "" : " ld" + s.Loads)}]";
    }

    public static readonly string[] Keys = ["A", "B"];

    public static Spec<State> Create()
        => Spec.From(State.Empty)
        .Print(s => s.ToString())

        .Action("Read", Keys, (s, k) => s.Read(k), weight: 6)
        .Action("Complete", Keys, (s, k) => s.Of(k).Loads > 0, (s, k) => s.Complete(k), weight: 3)
        .Action("Fail", Keys, (s, k) => s.Of(k).Loads > 0, (s, k) => s.Fail(k), weight: 1)
        .Action("Tick", s => s.Tick(), weight: 3)

        .Invariant("SINGLE-FLIGHT",
            "At most one load is in flight for a key at any time, however many callers read it.",
            s => s.A.Loads <= 1 && s.B.Loads <= 1)
        // Without this, a guard that accidentally prevented any load from completing would leave every requirement
        // below passing on a cache that never serves anything.
        .Reachable("CAN-SERVE-FRESH",
            "A value can be loaded and then served fresh, which is the case the cache exists for.",
            s => s.Served == Served.Fresh)
        // An invariant that the load count never goes negative was here. Faults reported that nothing could break it,
        // and nothing could: the Complete and Fail guards make it structurally true. It was a check on the encoding
        // dressed up as a requirement, so it is gone.

        .Rule("MISS-STARTS-LOAD",
            "A read that misses has a load in flight by the time it returns, so the caller is waiting on a load that "
            + "is actually running - joining one already in flight counts.",
            on: "Read",
            when: (_, a) => a.Served == Served.Miss,
            then: (_, a) => a.Of(a.Touched).Loads == 1)
        .Rule("STALE-REFRESHES",
            "A read that finds the value stale starts a load, so a value is refreshed by demand for it and not by a "
            + "timer.",
            on: "Read",
            when: (b, a) => b.Of(a.Touched).Stale && b.Of(a.Touched).Loads == 0,
            then: (_, a) => a.Started && a.Of(a.Touched).Loads == 1)
        .Rule("STALE-SERVED-ANYWAY",
            "A read that finds the value stale still returns it. Refreshing is what happens next, not what the "
            + "caller waits for.",
            on: "Read",
            when: (b, a) => b.Of(a.Touched).Present && b.Of(a.Touched).Stale,
            then: (_, a) => a.Served == Served.Stale)
        .Rule("NO-HERD",
            "A read never starts a second load for a key that is already loading.",
            on: "Read",
            when: (b, a) => b.Of(a.Touched).Loads > 0,
            then: (b, a) => !a.Started && a.Of(a.Touched).Loads == b.Of(a.Touched).Loads)
        .Rule("FAILURE-KEEPS-VALUE",
            "A load that fails leaves the cached value alone. Evicting on failure turns a slow dependency into an "
            + "outage.",
            on: "Fail",
            then: (b, a) => a.Of(a.Touched).Version == b.Of(a.Touched).Version
                         && a.Of(a.Touched).Age == b.Of(a.Touched).Age)
        .Rule("COMPLETE-IS-FRESH",
            "A load that completes leaves the value fresh, so the next read is served without starting another load.",
            on: "Complete",
            then: (_, a) => !a.Of(a.Touched).Stale && a.Of(a.Touched).Present)
        .Rule("CROSS-KEY-INDEPENDENT",
            "A load in flight for one key never stops another key being served. One shared lock over the whole cache "
            + "would break this and nothing else here would notice.",
            on: "Read",
            when: (b, a) => b.Of(a.Touched).Present && b.Of(Other(a.Touched)).Loads > 0,
            then: (_, a) => a.Served is Served.Fresh or Served.Stale)

        .Never("VERSION-MONOTONIC",
            "A cached value is never replaced by an older one, and never disappears once present.",
            (b, a) => a.A.Version < b.A.Version || a.B.Version < b.B.Version)
        // One instance per key. A single NeverAfter over both would remember only that some key had been loaded, and
        // loading A would discharge the obligation for a read of B - which exhaustive exploration finds in 3 steps.
        .NeverAfter("NEVER-MISS-TWICE",
            "Once a key has been loaded, no later read of it misses. This is the whole point of refreshing on access "
            + "rather than expiring: callers wait at most once per key, ever.",
            Keys,
            after: (_, a, k) => a.Of(k).Present,
            never: (_, a, k) => a.Served == Served.Miss && a.Touched == k)

        // There is deliberately no Response requirement here. Every liveness property this cache might have is the
        // environment's to deliver, not the cache's: a value only refreshes if something reads it and the loader
        // returns, and neither is bounded by anything the cache does. RefreshCache_Stale_Is_Unbounded_While_Loader_Fails
        // asserts the opposite so the counterexample is on the record.
        //
        // Response also could not be keyed correctly here even where the property held. Its trigger and response are
        // separate predicates over (before, after) with nothing carried between them, so "after a load starts for key
        // k, key k becomes fresh" has no way to remember which k, and `per:` names an action rather than an action and
        // its argument. A per-key requirement has to be written out once per key.

        .Fault("failure evicts the value",
            (b, a) => a.Served == Served.None && a.Of(a.Touched).Loads < b.Of(a.Touched).Loads
                   && a.Of(a.Touched).Version == b.Of(a.Touched).Version,
            (_, a) => a.Of(a.Touched) is { } slot && slot.Present ? Set(a, new Slot(0, 0, slot.Loads)) : a)
        .Fault("every read starts a load",
            (b, a) => a.Served != Served.None && !a.Started && (!b.Of(a.Touched).Present || b.Of(a.Touched).Stale),
            (_, a) => Set(a, a.Of(a.Touched).Start()) with { Started = true })
        .Fault("stale read does not refresh",
            (b, a) => a.Started && b.Of(a.Touched).Present,
            (b, a) => Set(a, a.Of(a.Touched) with { Loads = b.Of(a.Touched).Loads }) with { Started = false })
        // A blocked read returns nothing at all rather than a miss. Modelling it as a miss made this fault trip
        // MISS-STARTS-LOAD first, so CROSS-KEY-INDEPENDENT was never shown to catch anything.
        .Fault("one lock over the whole cache",
            (b, a) => a.Served is Served.Fresh or Served.Stale && b.Of(Other(a.Touched)).Loads > 0,
            (b, a) => Set(a, a.Of(a.Touched) with { Loads = b.Of(a.Touched).Loads })
                      with { Served = Served.None, Started = false })
        // Out of order completion: the load that finishes last was started first, so it writes back data older than
        // what is already there. Clamped above 1 so the value stays present, or COMPLETE-IS-FRESH catches it first
        // and this fault says nothing about VERSION-MONOTONIC.
        .Fault("a completing load writes an older value",
            (b, a) => a.Of(a.Touched).Version > b.Of(a.Touched).Version && b.Of(a.Touched).Version >= 2,
            (b, a) => Set(a, a.Of(a.Touched) with { Version = b.Of(a.Touched).Version - 1 }))
        .Fault("completing a load ages it",
            (b, a) => a.Served == Served.None && a.Of(a.Touched).Version > b.Of(a.Touched).Version,
            (_, a) => Set(a, a.Of(a.Touched) with { Age = Ttl }));

    static string Other(string k) => k == "A" ? "B" : "A";

    static State Set(State s, Slot slot) => s.Touched == "A" ? s with { A = slot } : s with { B = slot };
}
