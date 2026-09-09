namespace Tests;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;

/// <summary><see cref="PCG.ThreadPCG"/> is thread static, so each thread owns its own generator and nothing needs to
/// synchronise. <see cref="PCG.Next"/> relies on that: it is a non-atomic read-modify-write of the public
/// <c>State</c> field, so two threads drawing from one PCG read the same state, return the same number and then slip
/// apart, silently corrupting both sequences.
/// <para>The async samplers captured <c>PCG.ThreadPCG</c> once, outside their loop, and then awaited inside it with
/// <c>ConfigureAwait(false)</c>. Every iteration after the first could therefore resume on a different pool thread
/// while still driving the original thread's PCG - and the original thread, back in the pool, would pick up another
/// test's sample and draw from that same PCG.</para>
/// <para>Almost nothing notices, because a corrupted stream is still a plausible random stream. What noticed was
/// <c>Check.Equality</c>, whose first check is <c>gen.Clone()</c> - the one place in the library that draws a value
/// twice from one state and requires the two to agree. It failed roughly once in twenty full test runs, in whichever</para>
/// <c>Equality</c> test happened to be sharing a thread with an async sample, with a seed that never reproduced.</summary>
public class PcgThreadAffinityTests
{
    /// <summary>Records the first thread to draw from each PCG stream and counts every draw from another one. This
    /// asserts the invariant directly rather than waiting for a corrupted draw to be observed, so it does not depend
    /// on two threads happening to collide - only on a continuation resuming somewhere else, which is the norm.</summary>
    sealed class OwnerTrackingGen : Gen<int>
    {
        readonly ConcurrentDictionary<uint, int> _owner = new();
        int _crossed;
        public int Crossed => Volatile.Read(ref _crossed);
        public string? Example;

        public override int Generate(PCG pcg, Size? min, out Size size)
        {
            var thread = Environment.CurrentManagedThreadId;
            var owner = _owner.GetOrAdd(pcg.Stream, thread);
            if (owner != thread)
            {
                Interlocked.Increment(ref _crossed);
                Example ??= $"PCG stream {pcg.Stream} drawn from thread {thread} but owned by thread {owner}";
            }
            size = new Size(0);
            return (int)pcg.Next(100);
        }
    }

    [Test]
    public async Task SampleAsync_Assert_Draws_From_The_Current_Threads_PCG()
    {
        var gen = new OwnerTrackingGen();
        await gen.SampleAsync(static async _ => await Task.Yield(), iter: 20_000);
        TUnitX.WriteLine(gen.Example ?? "no cross-thread draws");
        await Assert.That(gen.Crossed).IsEqualTo(0);
    }

    [Test]
    public async Task SampleAsync_Predicate_Draws_From_The_Current_Threads_PCG()
    {
        var gen = new OwnerTrackingGen();
        await gen.SampleAsync(static async _ => { await Task.Yield(); return true; }, iter: 20_000);
        TUnitX.WriteLine(gen.Example ?? "no cross-thread draws");
        await Assert.That(gen.Crossed).IsEqualTo(0);
    }
}
