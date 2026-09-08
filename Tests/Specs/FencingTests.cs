namespace Tests.Specs;

using System.Threading.Tasks;
using CsCheck;

/// <summary>The same system in three configurations. Two of them fail their own safety requirement, and the shortest
/// counterexample for each is the argument for going one step further. The third closes - along with the requirement
/// that says the fix must not be the degenerate one.</summary>
public class FencingTests
{
    /// <summary>A lease alone does not give mutual exclusion at the resource. The shortest trace that loses an update
    /// is the scenario from the blog post, generated rather than drawn.</summary>
    [Test]
    public async Task Lease_Alone_Loses_Updates()
    {
        FencingSpec.Create(FencingSpec.Fence.None).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("NO-LOST-UPDATE");
        TUnitX.WriteLine(violation.ToString(s => s.ToString()));
    }

    /// <summary>Fencing only the writes is not enough, which is the interesting result. The resource learns a token
    /// only when one is presented, so a new holder that has not written yet leaves the old holder's token still the
    /// highest the resource has seen, and the old holder's late write is accepted.
    ///
    /// The shortest violation is not the lost update itself but its direct cause two steps earlier: a read on a
    /// superseded token being served. Both requirements fail for this configuration; breadth first finds the
    /// shallower one, which is also the more useful one to be told about.</summary>
    [Test]
    public async Task Writes_Only_Is_Not_Enough()
    {
        FencingSpec.Create(FencingSpec.Fence.Writes).Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("SUPERSEDED-TOKEN-REFUSED[1]");
        TUnitX.WriteLine(violation.ToString(s => s.ToString()));
    }

    /// <summary>Presenting the token on every access closes the state space: no reachable state of any interleaving
    /// loses an update, and no client that genuinely holds the lease is ever refused.</summary>
    [Test]
    public async Task Every_Access_Is_Safe()
    {
        var report = FencingSpec.Create(FencingSpec.Fence.Every).Exhaustive(TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        // Pinned so that "no counterexample was found" cannot be satisfied by a search that covered less than it did
        // before. Update deliberately, in the commit that changes the model.
        await Assert.That(report.States).IsEqualTo(583);
        await Assert.That(report.Transitions).IsEqualTo(3_022);
    }

    /// <summary>Every way of getting the token check subtly wrong, and which requirement notices. The last fault is
    /// the design mistake the second test above found, kept here so a later change cannot reintroduce it.</summary>
    [Test]
    public void Faults_Are_All_Caught()
    {
        FencingSpec.Create(FencingSpec.Fence.Every).Faults(TUnitX.WriteLine);
    }

    /// <summary>Faults cannot run at all on a model too large to close, which is exactly where the requirements are
    /// least proven, so SampleFaults walks each fault instead. Here it is held against the proof on a model that does
    /// close, from one spec object rather than two equal ones: every fault is found, and found by the same requirement.
    /// The step counts are legitimately weaker and so are not compared - sampling reports the shallowest counterexample
    /// it saw, not the shallowest there is.</summary>
    [Test]
    public async Task SampleFaults_Agrees_With_The_Proof()
    {
        var spec = FencingSpec.Create(FencingSpec.Fence.Every);
        var proved = spec.Faults();
        var sampled = spec.SampleFaults(TUnitX.WriteLine, maxSteps: 30, iter: 20_000, threads: 1);
        await Assert.That(sampled.Uncaught).IsEmpty();
        foreach (var result in proved.Results)
            await Assert.That(sampled.CaughtBy(result.Fault)).IsEqualTo(result.CaughtBy);
    }

    [Test]
    public async Task Sample()
    {
        var report = FencingSpec.Create(FencingSpec.Fence.Every).Sample(TUnitX.WriteLine, maxSteps: 30, iter: 20_000);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }
}
