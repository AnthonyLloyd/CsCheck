namespace Tests.Specs;

using System.Threading.Tasks;
using CsCheck;

/// <summary>A refresh-on-access cache, specified once and checked the same four ways as the FIX session layer. The
/// interesting difference is that the concurrency is in the specification rather than in the test runner: a load is
/// two actions with an arbitrary gap between them, so exhaustive exploration covers every interleaving.</summary>
public class RefreshCacheTests
{
    [Test]
    public async Task Exhaustive_Proof()
    {
        var report = RefreshCacheSpec.Create().Exhaustive(writeLine: TUnitX.WriteLine);
        await Assert.That(report.Closed).IsTrue();
        await Assert.That(report.DeadlockStates).IsEqualTo(0);
        await Assert.That(report.NeverTriggered).IsEmpty();
        await Assert.That(report.NeverFired).IsEmpty();
        // Pinned so that "no counterexample was found" cannot be satisfied by a search that covered less than it did
        // before. Update deliberately, in the commit that changes the model.
        await Assert.That(report.States).IsEqualTo(1_445);
        await Assert.That(report.Transitions).IsEqualTo(6_713);
    }

    [Test]
    public async Task Sample()
    {
        var report = RefreshCacheSpec.Create().Sample(TUnitX.WriteLine, maxSteps: 30, iter: 20_000);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    [Test]
    public void Faults_Are_All_Caught()
    {
        RefreshCacheSpec.Create().Faults(TUnitX.WriteLine);
    }

    /// <summary>An independently written implementation, driven down the same walk. Unlike the FIX engine this one has
    /// no planted defect, so the run completes and the coverage table shows what a clean conformance result looks
    /// like.</summary>
    [Test]
    public async Task Conforms_To_Spec()
    {
        static bool Apply(RefreshCache c, Transition<RefreshCacheSpec.State> t)
        {
            var key = RefreshCacheSpec.Keys[t.ArgIndex];
            var served = RefreshCache.Served.None;
            switch (t.Action)
            {
                case "Read": served = c.Read(key); break;
                case "Complete": c.Complete(key); break;
                case "Fail": c.Fail(key); break;
                default: c.Tick(); break;
            }
            return served == t.After.Served
                && c.Version(key) == t.After.Of(key).Version
                && c.Age(key) == t.After.Of(key).Age
                && c.Loads(key) == t.After.Of(key).Loads;
        }
        var report = RefreshCacheSpec.Create()
            .Conform(() => new RefreshCache(), Apply, TUnitX.WriteLine, maxSteps: 30, iter: 20_000);
        await Assert.That(report.NeverTriggered).IsEmpty();
    }

    /// <summary>The cache has no liveness property of its own: a stale value only becomes fresh if something reads it
    /// and the loader returns, and neither is bounded by anything the cache controls. Asserting the property anyway
    /// puts the counterexample on the record, which is the argument for whichever behaviour you choose to ship.
    /// <para>The requirement is stated per key. A single Response over both would carry one deadline between them, so a</para>
    /// refresh completing for one key would discharge the obligation raised by the other.</summary>
    [Test]
    public async Task Stale_Is_Unbounded_While_Loader_Fails()
    {
        RefreshCacheSpec.Create()
        .Response("STALE-ALWAYS-CLEARS",
            "A stale value always becomes fresh again.",
            RefreshCacheSpec.Keys,
            trigger: (_, a, k) => a.Started && a.Touched == k,
            response: (_, a, k) => !a.Of(k).Stale,
            within: RefreshCache.Ttl, per: "Tick")
        .Exhaustive(out var violation, TUnitX.WriteLine);
        await Assert.That(violation).IsNotNull();
        await Assert.That(violation!.Id).IsEqualTo("STALE-ALWAYS-CLEARS[A]");
        TUnitX.WriteLine(violation.ToString(s => s.ToString()));
    }
}
