namespace Tests.Specs;

using CsCheck;

/// <summary>A bounded buffer guarded by <c>wait</c> and <c>notify</c>, and the reason every code review says to write
/// <c>notifyAll</c>. A producer that finds the buffer full waits; a consumer that finds it empty waits; and each of
/// them, on succeeding, wakes one thread of the opposite kind. Nothing in that is obviously wrong, and it deadlocks.
/// <para>The specification is Markus Kuppe's <c>BlockingQueue</c> (github.com/lemmy/BlockingQueue), which is the canonical
/// demonstration of a model checker finding a real Java concurrency bug - the fairness constraint in it is Lamport's.
/// Two things make it worth having here rather than only there.</para>
/// <para>First, it is the one worked example that <em>deadlocks</em>. The other three prove a safety property over a design
/// that holds; this one has a design that does not, and the way it fails is that every thread ends up waiting for one
/// of the others. That is a state with no action enabled, so it needs no requirement at all to detect: the deadlock
/// count finds it and <c>DeadlockTrace</c> prints the path. The original states it as an invariant instead
/// (<c>waitSet # Producers \cup Consumers</c>), and both are worth seeing - see the tests.</para>
/// <para>Second, the original derives a closed form for when the bug bites: it is deadlock free exactly when
/// <c>2 * BufCapacity &gt;= Cardinality(Producers \cup Consumers)</c>. A prediction over a whole family of
/// configurations is a much stronger thing to check a reimplementation against than any single trace, and the tests
/// sweep it.</para>
/// <para>One abstraction, and it is the one the docs argue for. The original buffer is a sequence of the producer ids that
/// filled it, but nothing ever reads a value out of it: <c>Get</c> takes <c>Tail(buffer)</c> and discards the head,
/// and a notify picks any waiting thread rather than the one whose datum was consumed. So only the length can change
/// behaviour, and the length is what this carries. <c>BlockingQueueTests</c> measures what keeping the ids would have
/// cost.</para></summary>
public static class BlockingQueueSpec
{
    /// <summary>Which thread a successful <c>Put</c> or <c>Get</c> wakes. Three designs someone might actually write,
    /// and only two of them work.</summary>
    public enum Wake
    {
        /// <summary><c>Object.notify()</c>, which is the original's <c>Notify</c>: one arbitrary waiting thread, of
        /// <em>either</em> kind. You cannot choose, and that is the bug - a consumer's notify can wake another consumer
        /// and leave the producer asleep.</summary>
        Any,
        /// <summary><c>Object.notifyAll()</c>: every waiting thread. The fix everyone reaches for.</summary>
        All,
        /// <summary>One waiting thread of the opposite kind, which is what the original's later <c>NotifyOther</c>
        /// became. The fix you get from two condition variables rather than one monitor, and it wakes one thread
        /// instead of all of them.</summary>
        Other,
    }

    /// <summary>The buffer's length, and which threads are in the wait set as a bit per thread - producers in the low
    /// bits, consumers above them. A bitmask rather than a set because the state has to have value equality, and
    /// because "every thread is waiting" is then one comparison.</summary>
    public readonly record struct State(int Count, int Waiting);

    /// <summary>A thread acting, and which thread it wakes: -1 for none, which is the only case when there is nothing
    /// of the opposite kind to wake, or when waking all of them.</summary>
    public readonly record struct Act(int Thread, int Wakes)
    {
        public override string ToString() => Wakes < 0 ? $"t{Thread}" : $"t{Thread}->t{Wakes}";
    }

    /// <summary>The specification for one configuration. Producers are threads 0 to producers-1 and consumers follow
    /// them, so a bit test tells the two kinds apart without a second field.</summary>
    public static Spec<State> Create(Wake wake, int producers, int consumers, int capacity)
    {
        var producerMask = (1 << producers) - 1;
        var consumerMask = ((1 << consumers) - 1) << producers;
        var allMask = producerMask | consumerMask;

        // Which threads a notify may pick from. Any is the whole wait set, which is the bug; Other is the opposite kind.
        var putWakes = wake == Wake.Any ? allMask : consumerMask;
        var getWakes = wake == Wake.Any ? allMask : producerMask;

        // One case per (actor, woken) pair the original admits, and no more: the guard below rejects a pair naming a
        // thread that is not waiting, so a step here is exactly a step there rather than several that coincide.
        var puts = Pairs(0, producers, putWakes, producers + consumers, wake);
        var gets = Pairs(producers, consumers, getWakes, producers + consumers, wake);

        return Spec.From(new State(0, 0))
            .Print(s => Show(s, producers, consumers))
            .Action("Put", puts, (s, a) => Enabled(s, a, s.Count == capacity, wake, putWakes),
                                 (s, a) => Apply(s, a, s.Count == capacity, wake, +1))
            .Action("Get", gets, (s, a) => Enabled(s, a, s.Count == 0, wake, getWakes),
                                 (s, a) => Apply(s, a, s.Count == 0, wake, -1))
            // TypeInv in the original. Worth keeping because it is the requirement that would catch a bug in the
            // bitmask arithmetic below rather than in the algorithm being specified.
            .Invariant("TYPE-OK", "The buffer holds between zero and BufCapacity elements and the wait set holds threads.",
                s => s.Count >= 0 && s.Count <= capacity && (s.Waiting & ~allMask) == 0)
            .Reachable("CAN-FILL", "The buffer can reach its capacity.", s => s.Count == capacity)
            .Reachable("CAN-WAIT", "A thread can block.", s => s.Waiting != 0);
    }

    /// <summary>Every (actor, woken) pair, plus the actor alone for when there is nothing to wake. <c>All</c> wakes
    /// everything at once, so there is nothing to choose and it gets the actor alone only.</summary>
    static Act[] Pairs(int actorBase, int actorCount, int wakeMask, int threads, Wake wake)
    {
        var acts = new List<Act>();
        for (int i = 0; i < actorCount; i++)
        {
            acts.Add(new Act(actorBase + i, -1));
            if (wake != Wake.All)
                for (int j = 0; j < threads; j++)
                    if ((wakeMask & (1 << j)) != 0 && j != actorBase + i) acts.Add(new Act(actorBase + i, j));
        }
        return [.. acts];
    }

    /// <summary>Whether the original admits this step. <paramref name="atLimit"/> is full for a Put and empty for a Get,
    /// which is the one condition that decides between succeeding and blocking.</summary>
    static bool Enabled(State s, Act a, bool atLimit, Wake wake, int wakeMask)
    {
        if ((s.Waiting & (1 << a.Thread)) != 0) return false;
        // Blocking is the original's Wait, which notifies nobody, so there is nobody to name. Same for notifyAll, where
        // everything waiting is woken and there is no choice to enumerate.
        if (atLimit || wake == Wake.All) return a.Wakes < 0;
        // Succeeding under a single notify: name the waiting thread woken, or name none when none can be.
        return a.Wakes < 0 ? (s.Waiting & wakeMask) == 0 : (s.Waiting & (1 << a.Wakes)) != 0;
    }

    /// <summary>No wake mask needed here, unlike <see cref="Enabled"/>: notifyAll clears the whole wait set because that
    /// is what waking every thread on one monitor does, and a single notify clears the one thread the step names.</summary>
    static State Apply(State s, Act a, bool atLimit, Wake wake, int delta)
    {
        if (atLimit) return s with { Waiting = s.Waiting | (1 << a.Thread) };
        var woken = wake == Wake.All ? 0
                  : a.Wakes < 0 ? s.Waiting
                  : s.Waiting & ~(1 << a.Wakes);
        return new State(s.Count + delta, woken);
    }

    /// <summary>Public so a test can print a counterexample the way the report does, rather than as the raw bitmask.</summary>
    public static string Show(State s, int producers, int consumers)
    {
        var sb = new System.Text.StringBuilder("buffer=").Append(s.Count).Append(" waiting={");
        var first = true;
        for (int i = 0; i < producers + consumers; i++)
            if ((s.Waiting & (1 << i)) != 0)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(i < producers ? 'p' : 'c').Append(i < producers ? i : i - producers);
            }
        return sb.Append('}').ToString();
    }

    /// <summary>The original's closed form: deadlock free exactly when twice the buffer capacity is at least the
    /// number of threads.</summary>
    public static bool PredictedDeadlockFree(int producers, int consumers, int capacity)
        => 2 * capacity >= producers + consumers;
}
