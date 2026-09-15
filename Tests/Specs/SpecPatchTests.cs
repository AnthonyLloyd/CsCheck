namespace Tests.Specs;

using System.Threading.Tasks;
using CsCheck;

/// <summary>Tests for the bug fixes and improvements added by the patch to Spec.cs.</summary>
public class SpecPatchTests
{
    // ── Change 1: Mermaid — curly braces in record ToString break node labels ─

    [Test]
    public async Task Mermaid_CurlyBracesInRecordToString_AreEscaped()
    {
        // C# records produce ToString like "S { V = 0 }" which contains { and }.
        // In Mermaid flowchart syntax, { and } inside ["..."] node labels are parsed
        // as diamond/rhombus shape modifiers, breaking rendering in GitHub and VS Code.
        // The fix escapes them as HTML entities &#123; / &#125;.
        record S(int V);
        var spec = Spec.From(new S(0)).Action("Step", s => new S(s.V + 1));

        var mermaid = Check.Mermaid(spec);

        await Assert.That(mermaid).DoesNotContain("S { V =");   // no raw {
        await Assert.That(mermaid).Contains("&#123;");           // escaped {
        await Assert.That(mermaid).Contains("&#125;");           // escaped }
    }

    // ── Change 2: Fault on: action name ──────────────────────────────────────

    [Test]
    public async Task Fault_On_UnknownActionName_Throws()
    {
        // Mirrors the validation behaviour of Never(on: ...) — a fault that refers
        // to a non-existent action must throw at the first engine call, not silently pass.
        var spec = Spec.From(0)
            .Action("Inc", i => i + 1)
            .Never("NOT_NEGATIVE", "Value must stay non-negative.", (_, a) => a < 0)
            .Fault("broken", on: "NoSuchAction", perturb: (_, a) => a - 10);

        var ex = Assert.Throws<Exception>(() => Check.Faults(spec));
        await Assert.That(ex.Message).Contains("NoSuchAction");
    }

    [Test]
    public async Task Fault_On_ActionName_IsCaughtWhenThatActionFires()
    {
        // Fault scoped to "Inc" subtracts 1 from the result instead of adding 1.
        // When Inc fires from state 0: before=0, after=1, perturb returns -1 → NOT_NEGATIVE fires.
        var spec = Spec.From(0)
            .Action("Inc", i => i + 1)
            .Action("Dec", i => i > 0, i => i - 1)
            .Never("NOT_NEGATIVE", "Value must stay non-negative.", (_, a) => a < 0)
            .Fault("inc-broken", on: "Inc", perturb: (before, _) => before - 1);

        var report = Check.Faults(spec);
        await Assert.That(report.CaughtBy("inc-broken")).IsEqualTo("NOT_NEGATIVE");
    }

    // ── Change 5: Conform falls back to "SUT" for generic/compiler-generated type names ─

    [Test]
    public async Task Conform_ValueTupleSut_DisplaysAsSUT()
    {
        // ValueTuple`2 (and other generic/compiler-generated types like anonymous types) produce
        // unreadable names. Conform falls back to "SUT" when the type name contains a backtick.
        var spec = Spec.From(0).Action("Inc", i => i + 1);
        var report = Check.Conform(spec, create: () => (0, false), apply: (_, _) => true);
        await Assert.That(report.ToString()).Contains("Spec.Conform to");
        await Assert.That(report.ToString()).DoesNotContain("ValueTuple");
    }

    [Test]
    public async Task Fault_On_When_CombinesActionScopeAndPredicate()
    {
        // Fault scoped to "Inc" AND only when the result exceeds 5.
        // Perturb sets the value to -1, violating NOT_NEGATIVE.
        // Steps where Inc fires but result <= 5 are left untouched.
        var spec = Spec.From(0)
            .Action("Inc", i => i + 1)
            .Never("NOT_NEGATIVE", "Value must stay non-negative.", (_, a) => a < 0)
            .Fault("inc-overflow", on: "Inc", when: (_, a) => a > 5, perturb: (_, _) => -1);

        var report = Check.Faults(spec);
        await Assert.That(report.CaughtBy("inc-overflow")).IsEqualTo("NOT_NEGATIVE");
    }

    [Test]
    public async Task Fault_On_ActionName_DoesNotFireOnUnrelatedActions()
    {
        // Fault is scoped to "B", but "B" has a guard that never enables it.
        // With correct on: scoping, the fault never fires → it appears in Uncaught.
        // Without scoping (bug), the fault would fire on "A" too, producing a violation
        // and moving the fault to Caught — wrong behaviour.
        var spec = Spec.From(0)
            .Action("A", i => i + 1)                           // always fires, result stays positive
            .Action("B", _ => false, i => i - 100)             // never enabled
            .Never("NOT_NEGATIVE", "Value must stay non-negative.", (_, a) => a < 0)
            .Fault("b-broken", on: "B", perturb: (_, a) => a - 1000); // would violate NOT_NEGATIVE

        var report = Check.Faults(spec);
        await Assert.That(report.Uncaught).Contains("b-broken");
    }
}
