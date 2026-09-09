namespace Tests.Specs;

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Wake = BlockingQueueSpec.Wake;

/// <summary>Checked against the original three ways, in increasing order of how much it would take to fool.
/// <para>One, an independent breadth first walk transliterated straight from the TLA+ - a list of producer ids for the
/// buffer and a set of names for the wait set, no bitmasks and no abstraction - has to agree on whether the deadlock is
/// reachable and on how many steps it takes to get there.</para>
/// <para>Two, the trace lengths the original publishes for named configurations are pinned: p1c2b1 deadlocks in eight states
/// and p2c2b1 in nine, TLC counting the initial state as one.</para>
/// <para>Three, and this is the one worth having, the original derives a closed form for when the design is broken -
/// deadlock free exactly when twice the capacity is at least the number of threads. That is a claim about a whole
/// family rather than about one trace, so the sweep checks this reimplementation against a theorem.</para>
/// <para>Reading the original mattered. Its final version notifies one thread of the <em>opposite</em> kind, which is
/// already a fix, and a model of that finds nothing - as the first attempt here did, oracle and all. The version the
/// published traces come from notifies one arbitrary thread of <em>either</em> kind, because that is what</para>
/// <c>Object.notify</c> does, and that is the whole bug.</summary>
public class BlockingQueueTests
{
    /// <summary>The bug, found without writing a requirement for it. Every thread waiting is a state with no action
    /// enabled, and no state here is a legitimate end - a running thread always has either a Put or a Get to do - so
    /// nothing is declared Terminal and every dead end is a real one. The deadlock count says one exists and
    /// DeadlockTrace says which, which is the difference between knowing the design can hang and being able to read the
    /// interleaving that hangs it.</summary>
    [Test]
    public async Task Notify_Deadlocks()
    {
        var report = BlockingQueueSpec.Create(Wake.Any, producers: 2, consumers: 2, capacity: 1)
            .Exhaustive(TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsGreaterThan(0);
        await Assert.That(report.TerminalStates).IsEqualTo(0);
        await Assert.That(report.DeadlockTrace).Contains("waiting={p0,p1,c0,c1}");
        await Assert.That(report.DeadlockTrace).Contains("no action enabled");
    }

    /// <summary>Both fixes, proved rather than assumed, and they are different fixes: notifyAll wakes everything, while
    /// waking one thread of the opposite kind wakes exactly one. Each closes with nothing stuck.</summary>
    [Test]
    [Arguments(Wake.All)]
    [Arguments(Wake.Other)]
    public async Task The_Fixes_Do_Not_Deadlock(Wake wake)
    {
        var report = BlockingQueueSpec.Create(wake, producers: 2, consumers: 2, capacity: 1)
            .Exhaustive(TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        await Assert.That(report.DeadlockTrace).IsNull();
    }

    /// <summary>The same bug the way the original states it, as an invariant over the wait set rather than as a dead
    /// end. Both are worth seeing: the invariant names the requirement and gives a shortest counterexample against it,
    /// the deadlock count needs no requirement at all. A Spec is not frozen until an engine runs it, so a test can add
    /// a requirement to one like this.</summary>
    [Test]
    [Arguments(1, 2, 1, 7)] // p1c2b1: the original's eight state trace
    [Arguments(2, 2, 1, 8)] // p2c2b1: nine states
    public async Task Published_Trace_Lengths(int producers, int consumers, int capacity, int steps)
    {
        var all = (1 << (producers + consumers)) - 1;
        BlockingQueueSpec.Create(Wake.Any, producers, consumers, capacity)
            .Invariant("NO-DEADLOCK", "Not every thread may be in the wait set at once.", s => s.Waiting != all)
            .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NO-DEADLOCK");
        // TLC counts the initial state, so its published length is one more than the number of steps.
        await Assert.That(violation.Trace.Steps.Length).IsEqualTo(steps);
        TUnitX.WriteLine(violation.ToString(s => BlockingQueueSpec.Show(s, producers, consumers)));
    }

    /// <summary>The strongest check available: the original derives that the design is deadlock free exactly when
    /// 2 * BufCapacity >= Cardinality(Producers \cup Consumers). Sweeping that predicts a yes or no for every
    /// configuration in the range, and a reimplementation that is subtly wrong will disagree somewhere. Both fixes are
    /// swept alongside, where the answer is no deadlock whatever the shape - which the closed form does not cover.</summary>
    [Test]
    public async Task Deadlock_Matches_The_Published_Closed_Form()
    {
        var rows = 0;
        for (int p = 1; p <= 3; p++)
            for (int c = 1; c <= 3; c++)
                for (int b = 1; b <= 3; b++)
                {
                    var report = BlockingQueueSpec.Create(Wake.Any, p, c, b).Exhaustive(maxStates: 100_000);
                    var free = report.DeadlockStates == 0;
                    var predicted = BlockingQueueSpec.PredictedDeadlockFree(p, c, b);
                    TUnitX.WriteLine($"p{p}c{c}b{b}  {report.States,4} states {report.Transitions,6} transitions  "
                        + $"deadlock free {free,-5} predicted {predicted,-5} {(free == predicted ? "agree" : "DISAGREE")}");
                    await Assert.That(free).IsEqualTo(predicted);
                    await Assert.That(report.Closed).IsTrue();
                    foreach (var fix in new[] { Wake.All, Wake.Other })
                        await Assert.That(BlockingQueueSpec.Create(fix, p, c, b)
                            .Exhaustive(maxStates: 100_000).DeadlockStates).IsEqualTo(0);
                    rows++;
                }
        await Assert.That(rows).IsEqualTo(27);
    }

    /// <summary>An independent walk of the original, transliterated rather than modelled: the buffer is a list of the
    /// producer ids that filled it and the wait set is a set of names, so nothing here shares an assumption or a line
    /// of bit arithmetic with the specification. It has to agree on the shortest number of steps to a deadlock.
    /// <para>It also prices the one abstraction. This keeps the producer ids the original carries; the specification keeps
    /// only the length, because nothing ever reads a value out of the buffer - Get takes the tail and discards the head,</para>
    /// and a notify picks a thread rather than the datum's owner. The ratio is what that distinction would have cost.</summary>
    [Test]
    public async Task Agrees_With_A_Transliteration_Of_The_Original()
    {
        for (int p = 1; p <= 3; p++)
            for (int c = 1; c <= 3; c++)
                for (int b = 1; b <= 2; b++)
                {
                    var (oracleStates, oracleDepth) = Brute(p, c, b);
                    var all = (1 << (p + c)) - 1;
                    var report = BlockingQueueSpec.Create(Wake.Any, p, c, b).Exhaustive(maxStates: 100_000);
                    BlockingQueueSpec.Create(Wake.Any, p, c, b)
                        .Invariant("D", "Not every thread may be waiting.", s => s.Waiting != all)
                        .Exhaustive(out var violation, maxStates: 100_000);
                    var depth = violation?.Trace.Steps.Length ?? -1;
                    TUnitX.WriteLine($"p{p}c{c}b{b}  oracle {oracleStates,4} states, deadlock at {oracleDepth,2}   "
                        + $"spec {report.States,4} states, deadlock at {depth,2}   "
                        + $"ids would cost x{oracleStates / (double)report.States:0.0}");
                    await Assert.That(depth).IsEqualTo(oracleDepth);
                }
    }

    /// <summary>Breadth first over the original's own state: <c>buffer</c> a sequence of producer ids and
    /// <c>waitSet</c> a set of thread names. Returns the distinct state count and the shortest number of steps to every
    /// thread waiting, or -1 when that is unreachable.</summary>
    static (int States, int DeadlockDepth) Brute(int producers, int consumers, int capacity)
    {
        var threads = Enumerable.Range(0, producers).Select(i => "p" + i)
            .Concat(Enumerable.Range(0, consumers).Select(i => "c" + i)).ToArray();
        var start = ("", "");
        var seen = new Dictionary<(string Buffer, string WaitSet), int> { [start] = 0 };
        var queue = new Queue<(string Buffer, string WaitSet)>();
        queue.Enqueue(start);
        var deadlock = -1;
        while (queue.Count != 0)
        {
            var node = queue.Dequeue();
            var depth = seen[node];
            var buffer = node.Buffer.Length == 0 ? [] : node.Buffer.Split(',');
            var waiting = node.WaitSet.Length == 0 ? [with(StringComparer.Ordinal)]
                        : new HashSet<string>(node.WaitSet.Split(','), StringComparer.Ordinal);
            if (waiting.Count == threads.Length && (deadlock < 0 || depth < deadlock)) deadlock = depth;
            // Next picks a thread out of RunningThreads, so a waiting thread does nothing.
            foreach (var t in threads.Where(t => !waiting.Contains(t)))
                foreach (var next in Successors(buffer, waiting, t, capacity, threads))
                    if (seen.TryAdd(next, depth + 1)) queue.Enqueue(next);
        }
        return (seen.Count, deadlock);
    }

    /// <summary>Every successor the original's Put or Get admits from one state for one thread, including which waiting
    /// thread the notify wakes. Notify picks from the whole wait set, of either kind, which is the bug.</summary>
    static IEnumerable<(string, string)> Successors(string[] buffer, HashSet<string> waiting, string t, int capacity,
        string[] threads)
    {
        var isProducer = t[0] == 'p';
        var room = isProducer ? buffer.Length < capacity : buffer.Length != 0;
        if (!room)
        {
            // Wait(t): joins the wait set, buffer unchanged, nobody notified.
            yield return (string.Join(",", buffer), Key([.. waiting, t], threads));
            yield break;
        }
        // Put(t, t) appends the producer's own id; Get(t) takes Tail and never looks at the head.
        var next = string.Join(",", isProducer ? [.. buffer, t] : buffer.Skip(1));
        if (waiting.Count == 0)
        {
            yield return (next, "");
            yield break;
        }
        foreach (var x in waiting)
            yield return (next, Key(waiting.Where(w => !string.Equals(w, x, StringComparison.Ordinal)), threads));
    }

    /// <summary>A canonical string for a wait set, so the same set is the same key however it was built.</summary>
    static string Key(IEnumerable<string> set, string[] threads)
    {
        var have = new HashSet<string>(set, StringComparer.Ordinal);
        return string.Join(",", threads.Where(have.Contains));
    }
}
