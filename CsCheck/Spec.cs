// Copyright 2026 Anthony Lloyd
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace CsCheck;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

/// <summary>One step of a <see cref="Trace{S}"/>: the action applied and the model state either side of it. This is
/// what <c>Conform</c> hands to its <c>apply</c>, so it carries both the printable names and the indices needed to
/// recover the typed argument.</summary>
/// <param name="Index">Zero based position of this step in the trace.</param>
/// <param name="ActionIndex">Position of the action in the order they were declared on the <see cref="Spec{S}"/>.</param>
/// <param name="ArgIndex">Index into the array passed as the action's domain, so a conformance test can recover the
/// typed argument as <c>domain[ArgIndex]</c>. Zero for an action declared without one.</param>
/// <param name="Action">The action's declared name, as it appears in reports.</param>
/// <param name="Arg">The argument rendered by <c>ToString</c>, or empty for an action declared without one.</param>
/// <param name="Before">The model state the action was applied to.</param>
/// <param name="After">The model state it produced, after any injected <c>Fault</c>.</param>
public readonly record struct Transition<S>(int Index, int ActionIndex, int ArgIndex, string Action, string Arg, S Before, S After)
{
    /// <summary>The action as it appears in a trace: <c>Name</c>, or <c>Name(Arg)</c> when it has an argument.</summary>
    public override string ToString() => Arg.Length == 0 ? Action : $"{Action}({Arg})";
}

/// <summary>A sequence of <see cref="Transition{S}"/> generated from a <see cref="Spec{S}"/>.</summary>
public sealed class Trace<S>
{
    /// <summary>The model state the trace starts from.</summary>
    public readonly S Initial;
    /// <summary>The steps in order. Shorter than the length asked for when the walk deadlocked.</summary>
    public readonly Transition<S>[] Steps;
    /// <summary>True when the walk stopped early because no action was enabled.</summary>
    public readonly bool Deadlocked;

    internal Trace(S initial, Transition<S>[] steps, bool deadlocked)
    {
        Initial = initial;
        Steps = steps;
        Deadlocked = deadlocked;
    }

    /// <summary>The trace with each state rendered by <paramref name="print"/>, one step per line.</summary>
    /// <param name="print">How to render a model state.</param>
    /// <param name="markStep">Zero based step to mark with <c>&gt;&gt;</c>, or -1 to mark none.</param>
    public string ToString(Func<S, string> print, int markStep = -1)
    {
        var sb = new StringBuilder();
        // Two at least so a short trace matches the docs, wider when the step numbers need it, and the state lines
        // follow the width so they stay level with the action names.
        var width = Math.Max(2, Steps.Length.ToString().Length);
        var indent = new string(' ', width + 7);
        sb.Append('\n').Append(indent).Append(print(Initial));
        for (int i = 0; i < Steps.Length; i++)
        {
            sb.Append('\n').Append(i == markStep ? " >> " : "    ").Append((i + 1).ToString().PadLeft(width))
              .Append(' ').Append(Steps[i].ToString()).Append('\n').Append(indent).Append(print(Steps[i].After));
        }
        if (Deadlocked) sb.Append('\n').Append(indent).Append("(no action enabled - trace ends here)");
        return sb.ToString();
    }

    /// <summary>The trace with each state rendered by <typeparamref name="S"/>'s own <c>ToString</c>.</summary>
    public override string ToString() => ToString(s => s?.ToString() ?? "null", -1);
}

/// <summary>A requirement that failed, the step it failed on and the trace that got there.</summary>
public sealed class SpecViolation<S>
{
    /// <summary>The id the requirement was declared with, for asserting which requirement caught something.</summary>
    public readonly string Id;
    /// <summary>The sentence from the document, as declared.</summary>
    public readonly string Quote;
    /// <summary>How it failed, in words - "triggered but no response within 3 steps".</summary>
    public readonly string Detail;
    /// <summary>Zero based step index, or -1 for the initial state.</summary>
    public readonly int StepIndex;
    /// <summary>The trace that reached the failure, ending on the step that caused it.</summary>
    public readonly Trace<S> Trace;

    internal SpecViolation(string id, string quote, string detail, int stepIndex, Trace<S> trace)
    {
        Id = id;
        Quote = quote;
        Detail = detail;
        StepIndex = stepIndex;
        Trace = trace;
    }

    /// <summary>The requirement, its quote and the trace, with each state rendered by <paramref name="print"/> and the
    /// failing step marked.</summary>
    /// <param name="print">How to render a model state.</param>
    public string ToString(Func<S, string> print)
        => new StringBuilder()
            .Append("\n    Requirement: ").Append(Id).Append(" - ").Append(Detail)
            .Append("\n          Spec: \"").Append(Quote).Append('"')
            .Append("\n         Trace: ").Append(Trace.ToString(print, StepIndex))
            .ToString();

    /// <summary>The violation with each state rendered by <typeparamref name="S"/>'s own <c>ToString</c>.</summary>
    public override string ToString() => ToString(s => s?.ToString() ?? "null");
}

enum ReqKind { Invariant, Reachable, Rule, Never, Response, Precedes, NeverAfter, AtMost }

sealed class Requirement<S>(ReqKind kind, string id, string quote)
{
    public readonly ReqKind Kind = kind;
    public readonly string Id = id;
    public readonly string Quote = quote;
    public Func<S, bool>? Holds;
    public Func<S, S, bool>? Trigger;
    public Func<S, S, bool>? Consequent;
    public Func<S, S, bool>? Cancel;
    // NeverAfter's until. Separate from Cancel, which discharges a Response's deadline rather than closing a scope.
    public Func<S, S, bool>? Until;
    public string? OnAction;
    public int OnActionIndex = -1;
    public string? PerAction;
    public int PerActionIndex = -1;
    // Response's within and AtMost's times. One field because they are the same kind of small bound and no form has both.
    public int Within;
    // Which byte of the two counter words this requirement owns, as a bit offset into their concatenation: 0 to 63 is
    // the deadlines word, 64 to 127 the counts word. Response and AtMost draw from one pool of sixteen such bytes, so
    // a spec can spend them in any mix rather than eight of each.
    public int Shift = -1;
    // Precedes and NeverAfter share one history word, and so share its sixty four bits between them.
    public ulong Bit;
}

sealed class SpecAction<S>(string name, int argCount, int weight, Func<int, string> argName, Func<S, int, bool> enabled, Func<S, int, S> apply)
{
    public readonly string Name = name;
    public readonly int ArgCount = argCount;
    public readonly int Weight = weight;
    public readonly Func<int, string> ArgName = argName;
    public readonly Func<S, int, bool> Enabled = enabled;
    public readonly Func<S, int, S> Apply = apply;
}

sealed class SpecFault<S>(string name, Func<S, S, bool> when, Func<S, S, S> perturb)
{
    public readonly string Name = name;
    public readonly Func<S, S, bool> When = when;
    public readonly Func<S, S, S> Perturb = perturb;
}

/// <summary>Entry point for building a <see cref="Spec{S}"/>.</summary>
public static class Spec
{
    /// <summary>Start a specification from an initial model state. <typeparamref name="S"/> must be immutable with
    /// value equality (a record or record struct) for <c>Exhaustive</c> to close the state space.</summary>
    public static Spec<S> From<S>(S initial) => new(initial);
}

/// <summary>An executable specification: a pure transition system plus named requirements quoted from a document.
/// The same object can be explored randomly (<c>Sample</c>), exhaustively
/// (<c>Exhaustive</c>), mutated (<c>Faults</c>) or run against a real
/// implementation (<c>Conform</c>).</summary>
/// <remarks>The builder methods add to this instance and return it, rather than returning a new Spec. So a Spec is
/// frozen the first time an engine runs it and adding to it after that throws: without that, one held in a static
/// field and added to by one test would silently change what every other test checked. Return a fresh Spec from a
/// method, as every example does, and derive variants from that.</remarks>
public sealed class Spec<S>(S initial)
{
    internal readonly S Initial = initial;
    internal readonly List<SpecAction<S>> Actions = [];
    internal readonly List<Requirement<S>> Requirements = [];
    internal readonly List<SpecFault<S>> FaultList = [];
    internal Func<S, string> Printer = s => s?.ToString() ?? "null";
    internal Func<S, bool>? IsTerminal;
    internal Func<S, bool>? InBoundary;
    // Named for the resource, not the form, because Response and AtMost share one pool of sixteen bytes across the two
    // counter words. A spec can spend them in any mix: twelve Responses and no AtMost costs the same node as six of each.
    internal int ByteSlots;
    internal int HistoryBits;
    internal bool HasResponse;
    // Which (action, argument) pair each action's arguments start at, so coverage is counted per case and not per
    // action. Built by Validate, which is also where the spec freezes.
    internal int[] ArgBase = [];
    internal int ArgPairs;
    internal bool Frozen;
    static readonly Func<int, string> NoArg = _ => "";

    void ThrowIfFrozen(string what)
    {
        if (Frozen) ThrowHelper.Throw(
            $"Spec cannot add {what} after it has been run. Return a fresh Spec from a method rather than sharing one.");
    }

    Spec<S> Add(Requirement<S> requirement)
    {
        ThrowIfFrozen($"requirement '{requirement.Id}'");
        Requirements.Add(requirement);
        return this;
    }

    /// <summary>How to render a model state in reports and counterexamples.</summary>
    public Spec<S> Print(Func<S, string> print)
    {
        ThrowIfFrozen("a printer");
        Printer = print;
        return this;
    }

    /// <summary>States where having no enabled action is the intended end of the trace, so they are reported
    /// separately from deadlocks. Without this every legitimately final state counts as a deadlock and the count
    /// is useless; with it, a non-zero deadlock count means the model can get stuck somewhere it should not.</summary>
    public Spec<S> Terminal(Func<S, bool> isTerminal)
    {
        ThrowIfFrozen("terminal states");
        IsTerminal = isTerminal;
        return this;
    }

    /// <summary>The region of the state space to explore. States reached from inside it are still checked, so a
    /// requirement violated by the step that leaves the boundary is still found; those states are just not expanded.
    /// Use this when a model has no sound abstraction that makes it finite: <c>Exhaustive</c> then still closes, and
    /// the result is the scoped claim "no violation is reachable without leaving the boundary" rather than the
    /// nothing you get from hitting <c>maxStates</c>. Prefer saturating a counter where you can - that makes higher
    /// values the same state, which generalises the proof instead of scoping it.</summary>
    public Spec<S> Boundary(Func<S, bool> inBoundary)
    {
        ThrowIfFrozen("a boundary");
        InBoundary = inBoundary;
        return this;
    }

    /// <summary>An action with no argument, always enabled.</summary>
    public Spec<S> Action(string name, Func<S, S> next, int weight = 1)
        => Action(name, static _ => true, next, weight);

    /// <summary>An action with no argument, enabled only when <paramref name="guard"/> holds. <paramref name="weight"/>
    /// is the relative probability of being picked by <c>Sample</c> among the enabled actions;
    /// <c>Exhaustive</c> ignores it. Raise it for actions that open up the state space and lower it for ones
    /// that end a trace, or a cheap always-enabled terminator will eat most of the sampling budget.</summary>
    public Spec<S> Action(string name, Func<S, bool> guard, Func<S, S> next, int weight = 1)
    {
        ThrowIfFrozen($"action '{name}'");
        if (weight < 1) ThrowHelper.Throw($"Spec Action '{name}' weight must be at least 1, was {weight}");
        Actions.Add(new SpecAction<S>(name, 1, weight, NoArg, (s, _) => guard(s), (s, _) => next(s)));
        return this;
    }

    /// <summary>An action over a small finite argument domain, always enabled. The domain is enumerated by
    /// <c>Exhaustive</c> and sampled by <c>Sample</c>, so declare abstract
    /// argument cases (for example TooLow/Expected/TooHigh) rather than raw values.</summary>
    public Spec<S> Action<T>(string name, T[] domain, Func<S, T, S> next, int weight = 1)
        => Action(name, domain, static (_, _) => true, next, weight);

    /// <summary>An action over a small finite argument domain, enabled only when <paramref name="guard"/> holds.</summary>
    public Spec<S> Action<T>(string name, T[] domain, Func<S, T, bool> guard, Func<S, T, S> next, int weight = 1)
    {
        ThrowIfFrozen($"action '{name}'");
        if (domain is null || domain.Length == 0) ThrowHelper.Throw($"Spec Action '{name}' domain is null or empty");
        if (weight < 1) ThrowHelper.Throw($"Spec Action '{name}' weight must be at least 1, was {weight}");
        Actions.Add(new SpecAction<S>(name, domain!.Length, weight, i => domain[i]?.ToString() ?? "null",
            (s, i) => guard(s, domain[i]), (s, i) => next(s, domain[i])));
        return this;
    }

    /// <summary>Must hold in the initial state and after every step.</summary>
    public Spec<S> Invariant(string id, string quote, Func<S, bool> holds)
    {
        return Add(new Requirement<S>(ReqKind.Invariant, id, quote) { Holds = holds });
    }

    /// <summary>Must hold in at least one reachable state. The dual of <c>Invariant</c>, and the guard against a model
    /// so over-constrained that it cannot reach the case you care about - which is the failure mode that makes every
    /// other requirement pass for the wrong reason. <c>Exhaustive</c> fails when the state space closes without
    /// this ever holding, which is a proof of unreachability; <c>Sample</c> can only report it as never seen.</summary>
    public Spec<S> Reachable(string id, string quote, Func<S, bool> holds)
    {
        return Add(new Requirement<S>(ReqKind.Reachable, id, quote) { Holds = holds });
    }

    /// <summary>Must hold over every step. The transition counterpart of <c>Invariant</c>, for a claim about what
    /// changed rather than about a single state, and reported as <c>every step</c> in the coverage table because there
    /// is no antecedent that could fail to fire. Prefer this to a <c>when</c> of <see langword="true"/>, which reports a count that
    /// looks like vacuity information and is only the number of steps evaluated.</summary>
    public Spec<S> Rule(string id, string quote, Func<S, S, bool> then)
    {
        return Add(new Requirement<S>(ReqKind.Rule, id, quote) { Consequent = then });
    }

    /// <summary>When <paramref name="when"/> holds over a step then <paramref name="then"/> must hold over the same step.</summary>
    public Spec<S> Rule(string id, string quote, Func<S, S, bool> when, Func<S, S, bool> then)
    {
        return Add(new Requirement<S>(ReqKind.Rule, id, quote) { Trigger = when, Consequent = then });
    }

    /// <summary>Whenever the action named <paramref name="on"/> is applied, <paramref name="then"/> must hold over that step.</summary>
    public Spec<S> Rule(string id, string quote, string on, Func<S, S, bool> then)
    {
        return Add(new Requirement<S>(ReqKind.Rule, id, quote) { OnAction = on, Consequent = then });
    }

    /// <summary>Whenever the action named <paramref name="on"/> is applied and <paramref name="when"/> holds,
    /// <paramref name="then"/> must hold over that step.</summary>
    public Spec<S> Rule(string id, string quote, string on, Func<S, S, bool> when, Func<S, S, bool> then)
    {
        return Add(new Requirement<S>(ReqKind.Rule, id, quote) { OnAction = on, Trigger = when, Consequent = then });
    }

    /// <summary>Must never hold over any step. Coverage counts steps evaluated rather than steps that could have
    /// failed, so this form cannot report vacuity; use the <c>on:</c> overload when you want that signal.</summary>
    public Spec<S> Never(string id, string quote, Func<S, S, bool> forbidden)
    {
        return Add(new Requirement<S>(ReqKind.Never, id, quote) { Consequent = forbidden });
    }

    /// <summary>Must never hold over a step applying the action named <paramref name="on"/>. Coverage counts how often
    /// that action ran, so a never that could not fire is reported rather than passing silently.</summary>
    public Spec<S> Never(string id, string quote, string on, Func<S, S, bool> forbidden)
    {
        return Add(new Requirement<S>(ReqKind.Never, id, quote) { OnAction = on, Consequent = forbidden });
    }

    /// <summary>May hold on at most <paramref name="times"/> steps of any one execution: "at most three retries", "the
    /// resource is created once". <c>Never</c> is the <paramref name="times"/> of zero case, expressed separately
    /// because it needs no counter.
    /// <para>The count so far becomes part of the search state, so this is proved rather than sampled: without that, a state
    /// reached once and a state reached for the fourth time would be the same search node and the excess would go</para>
    /// unreported. Costs one byte of node per requirement, so unlike <c>Precedes</c> the limit is eight.</summary>
    public Spec<S> AtMost(string id, string quote, int times, Func<S, S, bool> occurs)
    {
        if (times is < 0 or > 254) ThrowHelper.Throw($"Spec AtMost '{id}' times must be 0 to 254, was {times}");
        if (ByteSlots == 16) ThrowHelper.Throw($"Spec AtMost '{id}' exceeds the limit of 16 Response and AtMost requirements");
        return Add(new Requirement<S>(ReqKind.AtMost, id, quote)
        { Consequent = occurs, Within = times, Shift = ByteSlots++ * 8 });
    }

    /// <summary>One <c>AtMost</c> per element of <paramref name="over"/>, each with its own count, reported as
    /// <c>id[element]</c>. Needed whenever the subject of the requirement is one of several things: a single instance
    /// counts occurrences across all of them, so three retries of one key would exhaust the budget for another. Costs
    /// one of the eight AtMost slots per element.</summary>
    public Spec<S> AtMost<T>(string id, string quote, int times, T[] over, Func<S, S, T, bool> occurs)
    {
        if (over is null || over.Length == 0) ThrowHelper.Throw($"Spec AtMost '{id}' over is null or empty");
        foreach (var item in over!)
        {
            var t = item;
            AtMost($"{id}[{t?.ToString()}]", quote, times, (b, a) => occurs(b, a, t));
        }
        return this;
    }

    /// <summary>Bounded response. Once <paramref name="trigger"/> holds, <paramref name="response"/> must hold on one of
    /// the next <paramref name="within"/> steps, unless <paramref name="cancel"/> discharges the obligation first.
    /// The outstanding deadline becomes part of the search state so <c>Exhaustive</c> proves this too.
    /// <para>The next steps, not this one: a response holding on the trigger step itself does not discharge the obligation.
    /// That is stricter than the usual reading of leads-to, and the opposite of <c>Precedes</c>, which is satisfied by
    /// its two predicates holding on one step. So a property whose consequence happens <em>in</em> the triggering step,
    /// like answering a TestRequest with a Heartbeat, is a <c>Rule</c>; <c>Response</c> is for the ones that take</para>
    /// time.</summary>
    /// <remarks><paramref name="per"/> names an action, not an action and its argument. That is right for a clock,
    /// which is what it is nearly always used for, but it means a deadline cannot be measured in "reads of key k".
    /// Split such an action into separately named actions if you need that. Note also that a <paramref name="per"/>
    /// bound only constrains paths on which that action recurs: proving it says nothing about an execution that stops
    /// ticking, which is the right reading of "within three heartbeat intervals" but is worth being explicit about.</remarks>
    public Spec<S> Response(string id, string quote, Func<S, S, bool> trigger, Func<S, S, bool> response, int within,
        Func<S, S, bool>? cancel = null, string? per = null)
    {
        if (within is < 1 or > 254) ThrowHelper.Throw($"Spec Response '{id}' within must be 1 to 254, was {within}");
        if (ByteSlots == 16) ThrowHelper.Throw($"Spec Response '{id}' exceeds the limit of 16 Response and AtMost requirements");
        HasResponse = true;
        return Add(new Requirement<S>(ReqKind.Response, id, quote)
        { Trigger = trigger, Consequent = response, Cancel = cancel, Within = within, Shift = ByteSlots++ * 8, PerAction = per });
    }

    /// <summary>One <c>Response</c> per element of <paramref name="over"/>, each with its own deadline, reported as
    /// <c>id[element]</c>. Needed whenever the subject of the requirement is one of several things: a single
    /// requirement over a keyed store carries one deadline, so a response for one key discharges the obligation
    /// raised by another. Costs one of the eight Response slots per element.</summary>
    public Spec<S> Response<T>(string id, string quote, T[] over, Func<S, S, T, bool> trigger,
        Func<S, S, T, bool> response, int within, Func<S, S, T, bool>? cancel = null, string? per = null)
    {
        if (over is null || over.Length == 0) ThrowHelper.Throw($"Spec Response '{id}' over is null or empty");
        foreach (var item in over!)
        {
            var t = item;
            Response($"{id}[{t?.ToString()}]", quote, (b, a) => trigger(b, a, t),
                (b, a) => response(b, a, t), within, cancel is null ? null : (b, a) => cancel(b, a, t), per);
        }
        return this;
    }

    /// <summary><paramref name="second"/> must never hold over a step unless <paramref name="first"/> held at or before it.</summary>
    public Spec<S> Precedes(string id, string quote, Func<S, S, bool> first, Func<S, S, bool> second)
    {
        if (HistoryBits == 64) ThrowHelper.Throw($"Spec Precedes '{id}' exceeds the limit of 64 Precedes and NeverAfter requirements");
        return Add(new Requirement<S>(ReqKind.Precedes, id, quote)
        { Trigger = first, Consequent = second, Bit = 1UL << HistoryBits++ });
    }

    /// <summary>One <c>Precedes</c> per element of <paramref name="over"/>, each with its own history, reported as
    /// <c>id[element]</c>. See the <c>NeverAfter</c> overload for why a shared history is wrong when the requirement
    /// has more than one possible subject.</summary>
    public Spec<S> Precedes<T>(string id, string quote, T[] over, Func<S, S, T, bool> first, Func<S, S, T, bool> second)
    {
        if (over is null || over.Length == 0) ThrowHelper.Throw($"Spec Precedes '{id}' over is null or empty");
        foreach (var item in over!)
        {
            var t = item;
            Precedes($"{id}[{t?.ToString()}]", quote, (b, a) => first(b, a, t), (b, a) => second(b, a, t));
        }
        return this;
    }

    /// <summary>Once <paramref name="after"/> has held, <paramref name="never"/> must not hold on any later step.
    /// The mirror of <c>Precedes</c>, for the many specifications that say a state is reached and then never left:
    /// once initialised never uninitialised, once committed never rolled back, once a value is cached never a miss.
    /// Holding on the same step is not a violation.
    /// <para>Give <paramref name="until"/> and the obligation lifts again when it holds, and returns when
    /// <paramref name="after"/> next does - "not between one and the other, every time round". The opening and closing
    /// steps are both outside the scope. Before reaching for it, check whether the state already says whether the scope
    /// is open: a field costs the same search state, reads in the printed counterexample where a history bit does not,</para>
    /// and can be shared with other requirements. Every worked example is better off with the field.</summary>
    public Spec<S> NeverAfter(string id, string quote, Func<S, S, bool> after, Func<S, S, bool> never,
        Func<S, S, bool>? until = null)
    {
        if (HistoryBits == 64) ThrowHelper.Throw($"Spec NeverAfter '{id}' exceeds the limit of 64 Precedes and NeverAfter requirements");
        return Add(new Requirement<S>(ReqKind.NeverAfter, id, quote)
        { Trigger = after, Consequent = never, Until = until, Bit = 1UL << HistoryBits++ });
    }

    /// <summary>One <c>NeverAfter</c> per element of <paramref name="over"/>, each with its own history, reported as
    /// <c>id[element]</c>. Needed whenever the subject of the requirement is one of several things: a single
    /// requirement over a keyed store remembers only that <em>something</em> happened, so the history of one key
    /// discharges the obligation for another.</summary>
    public Spec<S> NeverAfter<T>(string id, string quote, T[] over, Func<S, S, T, bool> after, Func<S, S, T, bool> never,
        Func<S, S, T, bool>? until = null)
    {
        if (over is null || over.Length == 0) ThrowHelper.Throw($"Spec NeverAfter '{id}' over is null or empty");
        foreach (var item in over!)
        {
            var t = item;
            NeverAfter($"{id}[{t?.ToString()}]", quote, (b, a) => after(b, a, t),
                (b, a) => never(b, a, t), until is null ? null : (b, a) => until(b, a, t));
        }
        return this;
    }

    /// <summary>A deliberate defect used by <c>Faults</c> to check the requirements are strong enough.
    /// When <paramref name="when"/> holds over a step the resulting state is replaced by <paramref name="perturb"/>.</summary>
    public Spec<S> Fault(string name, Func<S, S, bool> when, Func<S, S, S> perturb)
    {
        ThrowIfFrozen($"fault '{name}'");
        FaultList.Add(new SpecFault<S>(name, when, perturb));
        return this;
    }

    /// <summary>Generator for random walks of the specification, for composing into other tests.</summary>
    public Gen<Trace<S>> GenTrace(int minSteps = 1, int maxSteps = 24)
    {
        Validate();
        return new GenSpecTrace<S>(this, minSteps, maxSteps);
    }

    // Resolve every on: action name to an index, so a typo fails loudly instead of a requirement
    // silently never firing. Called by every engine and idempotent.
    internal void Validate()
    {
        if (Actions.Count == 0) ThrowHelper.Throw("Spec has no actions");
        // Otherwise every state is pruned, the report says one state and closed, and it looks like a proof.
        if (InBoundary is not null && !InBoundary(Initial)) ThrowHelper.Throw("Spec boundary excludes the initial state");
        // Two actions with the same name make on: and per: resolution ambiguous: only the first match is found, so a
        // requirement scoped to that name silently misses every later action of the same name.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in Actions)
            if (!names.Add(a.Name)) ThrowHelper.Throw($"Spec has more than one action with the name '{a.Name}'");
        // Two requirements sharing an id would give the coverage table two identical rows and make Faults credit the
        // wrong one, quietly degrading the traceability the ids exist for.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in Requirements)
            if (!ids.Add(r.Id)) ThrowHelper.Throw($"Spec has more than one requirement with the id '{r.Id}'");
        foreach (var r in Requirements)
        {
            if (r.OnAction is not null && r.OnActionIndex == -1)
            {
                for (int a = 0; a < Actions.Count; a++)
                    if (string.Equals(Actions[a].Name, r.OnAction, StringComparison.Ordinal)) { r.OnActionIndex = a; break; }
                if (r.OnActionIndex == -1)
                    ThrowHelper.Throw($"Spec requirement '{r.Id}' refers to action '{r.OnAction}' which does not exist");
            }
            if (r.PerAction is not null && r.PerActionIndex == -1)
            {
                for (int a = 0; a < Actions.Count; a++)
                    if (string.Equals(Actions[a].Name, r.PerAction, StringComparison.Ordinal)) { r.PerActionIndex = a; break; }
                if (r.PerActionIndex == -1)
                    ThrowHelper.Throw($"Spec requirement '{r.Id}' refers to per action '{r.PerAction}' which does not exist");
            }
        }
        if (ArgBase.Length != Actions.Count)
        {
            ArgBase = new int[Actions.Count];
            var pairs = 0;
            for (int a = 0; a < Actions.Count; a++) { ArgBase[a] = pairs; pairs += Actions[a].ArgCount; }
            ArgPairs = pairs;
        }
        Frozen = true;
    }
}

sealed class GenSpecTrace<S>(Spec<S> spec, int minSteps, int maxSteps, SpecFault<S>? fault = null) : Gen<Trace<S>>
{
    [ThreadStatic] static int[]? actionBuf;
    [ThreadStatic] static int[]? argBuf;

    public override Trace<S> Generate(PCG pcg, Size? min, out Size size)
    {
        var actions = spec.Actions;
        var length = minSteps + (int)pcg.Next((uint)(maxSteps - minSteps + 1));
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var enabledActions = actionBuf;
        if (enabledActions is null || enabledActions.Length < actions.Count)
            enabledActions = actionBuf = new int[actions.Count];
        var steps = new Transition<S>[length];
        var state = spec.Initial;
        var deadlocked = false;
        int n = 0;
        for (; n < length; n++)
        {
            var na = 0;
            var weight = 0;
            for (int a = 0; a < actions.Count; a++)
            {
                var action = actions[a];
                for (int g = 0; g < action.ArgCount; g++)
                {
                    if (action.Enabled(state, g)) { enabledActions[na++] = a; weight += action.Weight; break; }
                }
            }
            if (na == 0) { deadlocked = true; break; }
            var ai = enabledActions[na - 1];
            var pick = (int)pcg.Next((uint)weight);
            for (int i = 0; i < na; i++)
            {
                pick -= actions[enabledActions[i]].Weight;
                if (pick < 0) { ai = enabledActions[i]; break; }
            }
            var chosen = actions[ai];
            var enabledArgs = argBuf;
            if (enabledArgs is null || enabledArgs.Length < chosen.ArgCount)
                enabledArgs = argBuf = new int[chosen.ArgCount];
            var ng = 0;
            for (int g = 0; g < chosen.ArgCount; g++)
                if (chosen.Enabled(state, g)) enabledArgs[ng++] = g;
            var gi = enabledArgs[(int)pcg.Next((uint)ng)];
            var after = chosen.Apply(state, gi);
            if (fault?.When(state, after) == true) after = fault.Perturb(state, after);
            steps[n] = new Transition<S>(n, ai, gi, chosen.Name, chosen.ArgName(gi), state, after);
            state = after;
            total.Add(new Size(((ulong)ai << 20) + (ulong)gi));
            if (Size.IsLessThan(min, size)) return default!;
        }
        size.I = (ulong)n << 32;
        if (n != length) System.Array.Resize(ref steps, n); // Gen<T>.Array shadows the type name here
        return new Trace<S>(spec.Initial, steps, deadlocked);
    }
}

/// <summary>The result of exploring a <see cref="Spec{S}"/>: coverage of actions and requirements, and for
/// <c>Exhaustive</c> whether the reachable state space was closed.</summary>
public sealed class SpecReport
{
    /// <summary>True when the whole reachable state space was enumerated, so safety and bounded response
    /// requirements are proved for the model rather than merely tested.</summary>
    public bool Closed { get; internal set; }
    /// <summary>Distinct states reached. Worth logging: a jump after a model change usually means an abstraction
    /// leaked.</summary>
    public int States { get; internal set; }
    /// <summary>Enabled (action, argument) pairs evaluated across every state, so every requirement was checked this
    /// many times. Counts a transition into an already seen state, which is why it exceeds <see cref="States"/>.</summary>
    /// <remarks>A <see langword="long"/>, unlike the state counts, because <see cref="States"/> is bounded by an <see langword="int"/>
    /// <c>maxStates</c> while this is that times the branching factor and so is not.</remarks>
    public long Transitions { get; internal set; }
    /// <summary>Transitions that reached a state already seen, so the search stopped rather than expanding it again.
    /// Zero on a space that closed means it is a tree, which is what the note about value equality turns on.</summary>
    public long Revisits { get; internal set; }
    /// <summary>Steps in the longest shortest-path from the initial state, so the depth at which the search finished.
    /// This bounds how long a counterexample can be, since the walk is breadth first.</summary>
    public int Depth { get; internal set; }
    /// <summary>States with no enabled action that were not declared <c>Terminal</c>. A model of a protocol should
    /// have none: anywhere else with nothing to do is a state the design cannot leave.</summary>
    public int DeadlockStates { get; internal set; }
    /// <summary>States with no enabled action that <c>Terminal</c> declared to be the intended end of a trace. Zero
    /// when no <c>Terminal</c> was declared, in which case every such state is counted a deadlock instead.</summary>
    public int TerminalStates { get; internal set; }
    /// <summary>A path to the first state that had nothing enabled and was not declared <c>Terminal</c>, rendered with
    /// the spec's printer, or null when there were none. <c>DeadlockStates</c> says a dead end exists; this says which,
    /// which is the difference between knowing the design can get stuck and knowing where.</summary>
    public string? DeadlockTrace { get; internal set; }
    /// <summary>States reached but not expanded because they fell outside the declared <c>Boundary</c>. When this is
    /// not zero, closure means "no violation is reachable without leaving the boundary", which is weaker than closure
    /// over the whole space, and an unheld <c>Reachable</c> requirement can no longer be called unreachable.</summary>
    public int Pruned { get; internal set; }
    /// <summary>Random walks completed. Zero for <c>Exhaustive</c>, which reports <c>States</c> and
    /// <c>Transitions</c> instead.</summary>
    public long TracesWalked { get; internal set; }
    /// <summary>Steps taken across every walk. Named apart from <see cref="Trace{S}.Steps"/>, which is one walk's
    /// transitions rather than a count of them.</summary>
    public long StepsWalked { get; internal set; }
    /// <summary>A diagnostic when the exploration could not finish or the model looks wrong.</summary>
    public string? Note { get; internal set; }

    internal string Mode = "";
    internal string[] ActionNames = [];
    internal long[] ActionFired = [];
    internal string[] RequirementIds = [];
    internal long[] RequirementTriggered = [];
    internal long[] RequirementUnresolved = [];
    // Whether the requirement has an antecedent that can fail to fire. An Invariant, and a
    // Never without on:, apply to every step, so their triggered count is just the number of steps
    // evaluated and says nothing about vacuity.
    internal bool[] RequirementGuarded = [];

    /// <summary>Requirements whose antecedent never fired, so they passed vacuously. Requirements that apply to
    /// every step have no antecedent to count and are not included.</summary>
    public IEnumerable<string> NeverTriggered
    {
        get
        {
            for (int i = 0; i < RequirementIds.Length; i++)
                if (RequirementGuarded[i] && RequirementTriggered[i] == 0) yield return RequirementIds[i];
        }
    }

    /// <summary>Actions that were never enabled, so part of the specification is dead.</summary>
    public IEnumerable<string> NeverFired
    {
        get
        {
            for (int i = 0; i < ActionNames.Length; i++)
                if (ActionFired[i] == 0) yield return ActionNames[i];
        }
    }

    /// <summary>The whole report: the closure line, any diagnostic note, and the requirement and action coverage
    /// tables. Pass no <c>writeLine</c> to an engine and print this instead if you would rather choose when.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder(Mode);
        if (States != 0)
        {
            sb.Append(!Closed ? "\n  state space NOT closed: "
                     : Pruned == 0 ? "\n  state space CLOSED: " : "\n  state space CLOSED within boundary: ")
              .Append(States.ToString("#,0")).Append(" states, ")
              .Append(Transitions.ToString("#,0")).Append(" transitions, depth ").Append(Depth)
              .Append(", ").Append(TerminalStates).Append(" terminal, ").Append(DeadlockStates).Append(" deadlock");
            if (Pruned != 0) sb.Append(", ").Append(Pruned.ToString("#,0")).Append(" outside");
        }
        if (TracesWalked != 0)
            sb.Append("\n  ").Append(TracesWalked.ToString("#,0")).Append(" traces, ")
              .Append(StepsWalked.ToString("#,0")).Append(" steps");
        if (Note is not null) sb.Append("\n  ").Append(Note);
        if (DeadlockTrace is not null) sb.Append("\n  first deadlock:").Append(DeadlockTrace);
        var w = 11;
        for (int i = 0; i < RequirementIds.Length; i++) if (RequirementIds[i].Length > w) w = RequirementIds[i].Length;
        for (int i = 0; i < ActionNames.Length; i++) if (ActionNames[i].Length > w) w = ActionNames[i].Length;
        sb.Append("\n  | ").Append("Requirement".PadRight(w)).Append(" |   Triggered | Unresolved |");
        for (int i = 0; i < RequirementIds.Length; i++)
        {
            sb.Append("\n  | ").Append(RequirementIds[i].PadRight(w)).Append(" | ")
              .Append((!RequirementGuarded[i] ? "every step" : RequirementTriggered[i] == 0 ? "NEVER"
                       : RequirementTriggered[i].ToString("#,0")).PadLeft(11)).Append(" | ")
              .Append((RequirementUnresolved[i] == 0 ? "" : RequirementUnresolved[i].ToString("#,0")).PadLeft(10)).Append(" |");
        }
        sb.Append("\n  | ").Append("Action".PadRight(w)).Append(" |       Fired |");
        for (int i = 0; i < ActionNames.Length; i++)
        {
            sb.Append("\n  | ").Append(ActionNames[i].PadRight(w)).Append(" | ")
              .Append((ActionFired[i] == 0 ? "NEVER" : ActionFired[i].ToString("#,0")).PadLeft(11)).Append(" |");
        }
        return sb.ToString();
    }
}

/// <summary>The three possible outcomes of one fault injection run.</summary>
public enum FaultOutcome
{
    /// <summary>A requirement detected the fault. <see cref="SpecFaultResult.CaughtBy"/> names it.</summary>
    Caught,
    /// <summary>The exhaustive search closed without finding a violation: the fault is proved undetectable in the
    /// model. <see cref="SpecFaultResult.CaughtBy"/> is null.</summary>
    NotDetected,
    /// <summary>The exhaustive search gave up before closing the state space, so it is unknown whether the fault
    /// is detectable. The fault appears in <see cref="SpecFaultsReport.Inconclusive"/>. Declare a
    /// <c>Boundary</c> or reduce the model to make the search conclusive.</summary>
    Inconclusive,
}

/// <summary>What became of one declared <c>Fault</c>: the outcome of the search, the requirement whose
/// counterexample was shortest when caught, and how many steps that took.</summary>
public readonly record struct SpecFaultResult(string Fault, FaultOutcome Outcome, string? CaughtBy, int Steps);

/// <summary>The result of <c>Faults</c>. Its <c>ToString</c> is the table, so a caller that only wants to read it
/// can pass no <c>writeLine</c> and print this instead.</summary>
public sealed class SpecFaultsReport
{
    /// <summary>One entry per declared fault, in declaration order.</summary>
    public IReadOnlyList<SpecFaultResult> Results { get; }
    /// <summary>Faults with outcome <see cref="FaultOutcome.NotDetected"/>: the exhaustive search closed without
    /// finding a violation, so each one means a requirement is missing. <c>Faults</c> throws on these unless
    /// <c>throwOnUncaught</c> is false.</summary>
    public IReadOnlyList<string> Uncaught { get; }
    /// <summary>Faults whose exhaustive search gave up before the state space closed, so it is not known whether
    /// the fault is detectable. These are not thrown on, because the search was incomplete; they show as NOT CLOSED
    /// in the table. Declare a <c>Boundary</c> or reduce the model to make the search conclusive.</summary>
    public IReadOnlyList<string> Inconclusive { get; }
    /// <summary>Requirements that no declared fault exercises: a list of faults worth writing rather than a failure.
    /// <c>Reachable</c> requirements are excluded, and not because a fault cannot break one - perturbing the model away
    /// from the state does exactly that, and <c>Exhaustive</c> then reports the <c>Reachable</c> as the violation. They
    /// are excluded because a fault written to do that says nothing about whether a requirement is strong enough, which
    /// is the only question this list is asking.</summary>
    public IReadOnlyList<string> Unexercised { get; }

    readonly string _table;

    internal SpecFaultsReport(IReadOnlyList<SpecFaultResult> results, IReadOnlyList<string> uncaught,
        IReadOnlyList<string> inconclusive, IReadOnlyList<string> unexercised, string table)
    {
        Results = results;
        Uncaught = uncaught;
        Inconclusive = inconclusive;
        Unexercised = unexercised;
        _table = table;
    }

    /// <summary>The requirement that caught <paramref name="fault"/>, or null when the outcome is
    /// <see cref="FaultOutcome.NotDetected"/> or <see cref="FaultOutcome.Inconclusive"/>. Throws when no fault
    /// of that name was declared, so a renamed or mistyped fault fails loudly rather than looking uncaught.</summary>
    public string? CaughtBy(string fault)
    {
        for (int i = 0; i < Results.Count; i++)
            if (string.Equals(Results[i].Fault, fault, StringComparison.Ordinal)) return Results[i].CaughtBy;
        throw new CsCheckException($"No fault named '{fault}' was declared");
    }

    /// <summary>The fault table: one row per declared fault, and the requirements no fault exercised.</summary>
    public override string ToString() => _table;
}

sealed class SpecCounters(int requirements, int actions)
{
    public readonly long[] Triggered = new long[requirements];
    public readonly long[] Unresolved = new long[requirements];
    public readonly long[] Fired = new long[actions];
    public long Traces;
    public long Steps;
}

readonly record struct SpecNode<S>(S State, ulong Deadlines, ulong Seen, ulong Counts);

// One expanded transition, computed during the parallel phase and consumed during the sequential one.
readonly record struct SpecEdge<S>(int Action, int Arg, S After, ulong Deadlines, ulong Seen, ulong Counts, string? Detail, int ReqIndex);

// The breadth first frontier and everything the two walks mutate.
// Holding this in a class costs nothing over locals: the Parallel.For lambda forces Roslyn to heap
// allocate a closure over exactly these fields anyway. Measured neutral - see Tests/SpecScaleTests.
sealed class SpecFrontier<S>(Spec<S> spec, SpecFault<S>? fault, SpecReport report, SpecCounters counters,
    int maxStates, int maxDepth)
{
    readonly Spec<S> _spec = spec;
    readonly SpecFault<S>? _fault = fault;
    readonly SpecReport _report = report;
    readonly SpecCounters _counters = counters;
    readonly List<SpecAction<S>> _actions = spec.Actions;
    readonly int _reqs = spec.Requirements.Count;
    readonly int[] _argBase = spec.ArgBase;
    readonly int _pairs = spec.ArgPairs;
    readonly int _maxStates = maxStates;
    readonly int _maxDepth = maxDepth;
    readonly List<int> _parent = [-1];
    readonly List<int> _edgeAction = [-1];
    readonly List<int> _edgeArg = [-1];
    readonly List<int> _depths = [0];
    readonly HashSet<SpecNode<S>> _visited = [new(spec.Initial, 0UL, 0UL, 0UL)];
    int _depth;
    long _revisits;
    int _firstDeadlock = -1;
    bool _stopped;
    bool _gaveUp;
    bool _truncated;
    SpecViolation<S>? _found;

    // The violation, or null. Set once; both walks stop inserting after it.
    public SpecViolation<S>? Found => _found;
    public bool GaveUp => _gaveUp;
    public bool Truncated => _truncated;
    // Transitions that reached an already seen state. One is proof the state's value equality works.
    public long Revisits => _revisits;
    public int States => Nodes.Count;
    public List<SpecNode<S>> Nodes { get; } = [new(spec.Initial, 0UL, 0UL, 0UL)];

    // Record one expanded transition, returning false when the walk must stop. Both walks call this
    // sequentially in source order, which is what makes the result independent of thread count.
    bool Insert(int head, in SpecEdge<S> edge)
    {
        _report.Transitions++;
        if (edge.Detail is not null)
        {
            var req = _spec.Requirements[edge.ReqIndex];
            _found = new SpecViolation<S>(req.Id, req.Quote, edge.Detail, _depth,
                Path(head, edge.Action, edge.Arg, edge.After));
            _report.Depth = _depth + 1;
            _report.States = Nodes.Count;
            return false;
        }
        var child = new SpecNode<S>(edge.After, edge.Deadlines, edge.Seen, edge.Counts);
        if (!_visited.Add(child)) { _revisits++; return true; }
        // The requirements were already checked on this transition, so the step out of the boundary is proved like any
        // other. Only the expansion of what it reached is given up, so it stays in the visited set and is counted once
        // however many paths reach it - which is what Pruned has always claimed to be.
        if (_spec.InBoundary is not null && !_spec.InBoundary(edge.After)) { _report.Pruned++; return true; }
        // The note is composed after the walk, where the final counters are available and this stays off the hot path.
        if (Nodes.Count == _maxStates)
        {
            _report.States = Nodes.Count;
            _report.Depth = _depth + 1;
            _gaveUp = true;
            return false;
        }
        Nodes.Add(child);
        _parent.Add(head);
        _edgeAction.Add(edge.Action);
        _edgeArg.Add(edge.Arg);
        _depths.Add(_depth + 1);
        return true;
    }

    // A node with nothing enabled is the intended end of a trace or a place the design cannot leave. The
    // first of the latter is remembered, because a count alone says a dead end exists without saying which.
    void Settle(int head)
    {
        if (_spec.IsTerminal?.Invoke(Nodes[head].State) == true) _report.TerminalStates++;
        else
        {
            if (_firstDeadlock < 0) _firstDeadlock = head;
            _report.DeadlockStates++;
        }
    }

    // The path to the first state that had nothing enabled and was not declared Terminal, or null if
    // there was none.
    public Trace<S>? DeadlockPath()
    {
        if (_firstDeadlock < 0) return null;
        var back = Backtrack(_firstDeadlock);
        var steps = new Transition<S>[back.Count];
        Replay(back, steps);
        return new Trace<S>(_spec.Initial, steps, true);
    }

    // The (action, argument) pairs from a node back to the initial state, so innermost first.
    List<(int Action, int Arg)> Backtrack(int head)
    {
        var back = new List<(int, int)>();
        for (int i = head; i > 0; i = _parent[i]) back.Add((_edgeAction[i], _edgeArg[i]));
        return back;
    }

    // Replay a backtracked path into steps, faults included, returning the state reached.
    // Only the walk knows how a state was arrived at, so a trace is rebuilt rather than stored.
    S Replay(List<(int Action, int Arg)> back, Transition<S>[] steps)
    {
        var state = _spec.Initial;
        for (int i = 0; i < back.Count; i++)
        {
            var (ai, arg) = back[back.Count - 1 - i];
            var action = _actions[ai];
            var after = action.Apply(state, arg);
            if (_fault?.When(state, after) == true) after = _fault.Perturb(state, after);
            steps[i] = new Transition<S>(i, ai, arg, action.Name, action.ArgName(arg), state, after);
            state = after;
        }
        return state;
    }

    // The path to a node, plus one more step onto the edge that violated a requirement.
    Trace<S> Path(int head, int lastAction, int lastArg, S lastAfter)
    {
        var back = Backtrack(head);
        var steps = new Transition<S>[back.Count + 1];
        var state = Replay(back, steps);
        var last = _actions[lastAction];
        steps[^1] = new Transition<S>(steps.Length - 1, lastAction, lastArg, last.Name, last.ArgName(lastArg), state, lastAfter);
        return new Trace<S>(_spec.Initial, steps, false);
    }

    // The default walk. Expansion and insertion are fused, so an edge is consumed while it is still in
    // registers and no buffer is touched. Measurably the fastest way to do this on one core.
    public void Sequential()
    {
        for (int head = 0; head < Nodes.Count && !_stopped; head++)
        {
            var node = Nodes[head];
            _depth = _depths[head];
            if (_depth > _report.Depth) _report.Depth = _depth;
            if (_depth == _maxDepth) { _truncated = true; continue; }
            var enabled = 0;
            // The parallel path expands a whole node before inserting any of it, so this one must finish the node too
            // or the two report different Fired and Triggered for the node a violation was found in - and NeverFired is
            // public API. Hence stop inserting, but keep evaluating.
            var stopInserting = false;
            for (int a = 0; a < _actions.Count; a++)
            {
                var action = _actions[a];
                for (int g = 0; g < action.ArgCount; g++)
                {
                    if (!action.Enabled(node.State, g)) continue;
                    enabled++;
                    _counters.Fired[_argBase[a] + g]++;
                    var after = action.Apply(node.State, g);
                    if (_fault?.When(node.State, after) == true) after = _fault.Perturb(node.State, after);
                    ulong d = node.Deadlines, s = node.Seen, k = node.Counts;
                    var det = Check.CheckTransition(_spec, a, node.State, after, ref d, ref s, ref k, _counters.Triggered, 0, out var r);
                    if (!stopInserting && !Insert(head, new SpecEdge<S>(a, g, after, d, s, k, det, r)))
                        stopInserting = true;
                }
            }
            if (stopInserting) _stopped = true;
            else if (enabled == 0) Settle(head);
        }
    }

    // Opt in. A frontier level is expanded in parallel into a buffer and then inserted sequentially. Only the
    // user delegates run in parallel; the visited set is never touched off the main thread. That buys nothing unless
    // those delegates dominate, because the sequential insert bounds the speedup - see
    // Tests/SpecScaleTests.Parallel_Speedup for the numbers.
    public void Parallel(int threads)
    {
        // At least one pair, because Validate rejects a spec with no actions and every action has at least one argument.
        var chunk = Math.Clamp(16384 / _pairs, 1, 4096);
        var edges = new SpecEdge<S>[chunk * _pairs];
        var edgeCount = new int[chunk];
        var enabledCount = new int[chunk];
        var triggered = new long[chunk * Math.Max(_reqs, 1)];
        var fired = new long[chunk * _pairs];
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        for (int levelStart = 0; levelStart < Nodes.Count && !_stopped;)
        {
            var levelEnd = Nodes.Count;
            _depth = _depths[levelStart];
            if (_depth > _report.Depth) _report.Depth = _depth;
            if (_depth == _maxDepth) { _truncated = true; break; }
            for (int chunkStart = levelStart; chunkStart < levelEnd && !_stopped; chunkStart += chunk)
            {
                var width = Math.Min(chunk, levelEnd - chunkStart);
                Array.Clear(triggered, 0, width * _reqs);
                Array.Clear(fired, 0, width * _pairs);
                var from = chunkStart;
                System.Threading.Tasks.Parallel.For(0, width, options, i =>
                {
                    var node = Nodes[from + i];
                    // Both buffers are strided by pairs, so one base serves both.
                    var slot = i * _pairs;
                    int n = 0, enabled = 0;
                    for (int a = 0; a < _actions.Count; a++)
                    {
                        var action = _actions[a];
                        for (int g = 0; g < action.ArgCount; g++)
                        {
                            if (!action.Enabled(node.State, g)) continue;
                            enabled++;
                            fired[slot + _argBase[a] + g]++;
                            var after = action.Apply(node.State, g);
                            if (_fault?.When(node.State, after) == true) after = _fault.Perturb(node.State, after);
                            ulong d = node.Deadlines, s = node.Seen, k = node.Counts;
                            var det = Check.CheckTransition(_spec, a, node.State, after, ref d, ref s, ref k, triggered, i * _reqs, out var r);
                            edges[slot + n++] = new SpecEdge<S>(a, g, after, d, s, k, det, r);
                        }
                    }
                    edgeCount[i] = n;
                    enabledCount[i] = enabled;
                });
                for (int i = 0; i < width && !_stopped; i++)
                {
                    var head = chunkStart + i;
                    for (int k = 0; k < _reqs; k++) _counters.Triggered[k] += triggered[i * _reqs + k];
                    for (int k = 0; k < _pairs; k++) _counters.Fired[k] += fired[i * _pairs + k];
                    if (enabledCount[i] == 0) { Settle(head); continue; }
                    var edgeBase = i * _pairs;
                    for (int k = 0; k < edgeCount[i]; k++)
                        if (!Insert(head, edges[edgeBase + k])) { _stopped = true; break; }
                }
            }
            levelStart = levelEnd;
        }
    }
}

public static partial class Check
{
    // Evaluate every requirement over a trace, updating the response deadline vector and the precedes mask.
    // Shared by the random and exhaustive engines so a proof and a sample agree exactly.
    // Trigger counts go into a flat array at triggerBase rather than into shared
    // counters, so the exhaustive engine can give each source node its own slice and evaluate a whole frontier in
    // parallel without any of them contending.
    internal static string? CheckTransition<S>(Spec<S> spec, int action, S before, S after, ref ulong deadlines, ref ulong seen,
        ref ulong counts, long[]? triggered, int triggerBase, out int reqIndex)
    {
        var requirements = spec.Requirements;
        for (reqIndex = 0; reqIndex < requirements.Count; reqIndex++)
        {
            var r = requirements[reqIndex];
            switch (r.Kind)
            {
                case ReqKind.Invariant:
                    if (triggered is not null) triggered[triggerBase + reqIndex]++;
                    if (!r.Holds!(after)) return "does not hold in the state reached";
                    break;
                case ReqKind.Reachable:
                    // Counts witnesses and never fails here. Unreachability is only a failure once the whole state
                    // space has been enumerated, which is the one place it can be concluded rather than guessed.
                    if (triggered is not null && r.Holds!(after)) triggered[triggerBase + reqIndex]++;
                    break;
                case ReqKind.Rule:
                    if (r.OnAction is not null && r.OnActionIndex != action) break;
                    if (r.Trigger is not null && !r.Trigger(before, after)) break;
                    if (triggered is not null) triggered[triggerBase + reqIndex]++;
                    if (!r.Consequent!(before, after))
                        return r.OnAction is null && r.Trigger is null ? "does not hold over the step"
                                                                      : "triggered but the required consequence did not happen";
                    break;
                case ReqKind.Never:
                    if (r.OnAction is not null && r.OnActionIndex != action) break;
                    if (triggered is not null) triggered[triggerBase + reqIndex]++;
                    if (r.Consequent!(before, after)) return "the forbidden step happened";
                    break;
                case ReqKind.Response:
                    // Response and AtMost draw byte slots from one pool spanning both counter words, so slots 0 to 7
                    // land in deadlines and 8 to 15 in counts whichever form claimed them.
                    ref ulong rw = ref r.Shift < 64 ? ref deadlines : ref counts;
                    var rs = r.Shift & 63;
                    var rem = (rw >> rs) & 0xFF;
                    if (rem != 0)
                    {
                        if (r.Cancel?.Invoke(before, after) == true || r.Consequent!(before, after)) rem = 0;
                        else if (r.PerAction is null || r.PerActionIndex == action)
                        {
                            if (--rem == 0) return r.PerAction is null
                                ? $"triggered but no response within {r.Within} steps"
                                : $"triggered but no response within {r.Within} '{r.PerAction}' steps";
                        }
                    }
                    if (r.Trigger!(before, after))
                    {
                        if (triggered is not null) triggered[triggerBase + reqIndex]++;
                        if (rem == 0) rem = (ulong)r.Within;
                    }
                    rw = (rw & ~(0xFFUL << rs)) | (rem << rs);
                    break;
                case ReqKind.AtMost:
                    if (!r.Consequent!(before, after)) break;
                    if (triggered is not null) triggered[triggerBase + reqIndex]++;
                    ref ulong aw = ref r.Shift < 64 ? ref deadlines : ref counts;
                    var ashift = r.Shift & 63;
                    var times = ((aw >> ashift) & 0xFF) + 1;
                    if (times > (ulong)r.Within) return $"happened more than {r.Within} times";
                    aw = (aw & ~(0xFFUL << ashift)) | (times << ashift);
                    break;
                case ReqKind.Precedes:
                    if (r.Trigger!(before, after)) { if (triggered is not null) triggered[triggerBase + reqIndex]++; seen |= r.Bit; }
                    if (r.Consequent!(before, after) && (seen & r.Bit) == 0) return "happened before the step that must precede it";
                    break;
                default: // NeverAfter
                    var open = (seen & r.Bit) != 0;
                    // until first, so a step that both closes and forbids is a close; then after, so a step that closes
                    // and reopens is a reopen. Both boundary steps are therefore outside the scope.
                    if (open && r.Until is not null && r.Until(before, after)) { seen &= ~r.Bit; open = false; }
                    if (open && r.Consequent!(before, after))
                        return r.Until is null ? "happened after the point it must not happen after"
                                               : "happened between the step that opens the scope and the step that closes it";
                    if (r.Trigger!(before, after)) { if (triggered is not null) triggered[triggerBase + reqIndex]++; seen |= r.Bit; }
                    break;
            }
        }
        return null;
    }

    static string? SpecInitial<S>(Spec<S> spec, SpecCounters? counters, out int reqIndex)
    {
        var requirements = spec.Requirements;
        for (reqIndex = 0; reqIndex < requirements.Count; reqIndex++)
        {
            var r = requirements[reqIndex];
            if (r.Kind == ReqKind.Invariant && !r.Holds!(spec.Initial)) return "does not hold in the initial state";
            if (r.Kind == ReqKind.Reachable && counters is not null && r.Holds!(spec.Initial)) counters.Triggered[reqIndex]++;
        }
        reqIndex = -1;
        return null;
    }

    static SpecViolation<S>? SpecCheck<S>(Spec<S> spec, Trace<S> trace, SpecCounters? counters)
    {
        var detail = SpecInitial(spec, counters, out var ri);
        if (detail is not null) return new SpecViolation<S>(spec.Requirements[ri].Id, spec.Requirements[ri].Quote, detail, -1, trace);
        ulong deadlines = 0, seen = 0, counts = 0;
        var steps = trace.Steps;
        for (int i = 0; i < steps.Length; i++)
        {
            detail = CheckTransition(spec, steps[i].ActionIndex, steps[i].Before, steps[i].After, ref deadlines, ref seen,
                ref counts, counters?.Triggered, 0, out ri);
            if (detail is not null)
                return new SpecViolation<S>(spec.Requirements[ri].Id, spec.Requirements[ri].Quote, detail, i, trace);
        }
        // Nothing to look for unless the spec has a Response, which only one of the worked examples has.
        if (counters is not null && spec.HasResponse)
        {
            for (int i = 0; i < spec.Requirements.Count; i++)
            {
                var r = spec.Requirements[i];
                if (r.Kind != ReqKind.Response) continue;
                var word = r.Shift < 64 ? deadlines : counts;
                if (((word >> (r.Shift & 63)) & 0xFF) != 0) counters.Unresolved[i]++;
            }
        }
        return null;
    }

    // Per trace tallies. They exist so the interlocked adds are one per counter per trace rather than one per
    // step, and reusing them rather than allocating three arrays each time is 29% of what a walk put on the heap -
    // measured, 2,140 down to 1,515 bytes a trace on the FIX example. Thread static like the buffers in
    // GenSpecTrace<S>, and only ever read between a clear and a flush inside one call.
    [ThreadStatic] static SpecCounters? _tally;

    // Check one walked trace and fold its coverage into the shared counters. Shared by Sample and
    // Conform, so the two report coverage identically.
    static SpecViolation<S>? SpecWalk<S>(Spec<S> spec, Trace<S> trace, SpecCounters counters)
    {
        Interlocked.Increment(ref counters.Traces);
        Interlocked.Add(ref counters.Steps, trace.Steps.Length);
        var tally = _tally;
        if (tally is null || tally.Triggered.Length != counters.Triggered.Length || tally.Fired.Length != counters.Fired.Length)
            tally = _tally = new SpecCounters(counters.Triggered.Length, counters.Fired.Length);
        else
        {
            Array.Clear(tally.Triggered);
            Array.Clear(tally.Unresolved);
            Array.Clear(tally.Fired);
        }
        var steps = trace.Steps;
        var argBase = spec.ArgBase;
        for (int i = 0; i < steps.Length; i++) tally.Fired[argBase[steps[i].ActionIndex] + steps[i].ArgIndex]++;
        var violation = SpecCheck(spec, trace, tally);
        for (int i = 0; i < tally.Triggered.Length; i++)
        {
            if (tally.Triggered[i] != 0) Interlocked.Add(ref counters.Triggered[i], tally.Triggered[i]);
            if (tally.Unresolved[i] != 0) Interlocked.Add(ref counters.Unresolved[i], tally.Unresolved[i]);
        }
        for (int i = 0; i < tally.Fired.Length; i++)
            if (tally.Fired[i] != 0) Interlocked.Add(ref counters.Fired[i], tally.Fired[i]);
        return violation;
    }

    static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    static SpecReport SpecReportOf<S>(Spec<S> spec, string mode, SpecCounters c)
    {
        var report = new SpecReport
        {
            Mode = mode,
            // The arrays below are shared by reference, so the report sees the walk as it happens. TracesWalked and
            // StepsWalked are values, so they cannot be; Sample and Conform copy them across once the walk is done.
            ActionNames = new string[spec.ArgPairs],
            ActionFired = c.Fired,
            RequirementIds = new string[spec.Requirements.Count],
            RequirementTriggered = c.Triggered,
            RequirementUnresolved = c.Unresolved,
            RequirementGuarded = new bool[spec.Requirements.Count],
        };
        // One row per (action, argument) case rather than per action. Counting per action hid a dead argument case
        // behind a busy total - FIX has twenty inbound cases behind one Recv, and NeverFired could not see any of them.
        for (int a = 0; a < spec.Actions.Count; a++)
        {
            var action = spec.Actions[a];
            for (int g = 0; g < action.ArgCount; g++)
            {
                var arg = action.ArgName(g);
                report.ActionNames[spec.ArgBase[a] + g] = arg.Length == 0 ? action.Name : $"{action.Name}({arg})";
            }
        }
        for (int i = 0; i < spec.Requirements.Count; i++)
        {
            var r = spec.Requirements[i];
            report.RequirementIds[i] = r.Id;
            report.RequirementGuarded[i] = r.Kind switch
            {
                ReqKind.Invariant => false,
                ReqKind.Never => r.OnAction is not null,
                ReqKind.Rule => r.OnAction is not null || r.Trigger is not null,
                _ => true,
            };
        }
        return report;
    }

    /// <summary>Randomly walk the specification checking every requirement on every step, shrinking any violation to
    /// the shortest and most ordinary trace that still fails.</summary>
    /// <param name="spec">The specification to explore.</param>
    /// <param name="writeLine">WriteLine function for the coverage report.</param>
    /// <param name="minSteps">The shortest trace to generate.</param>
    /// <param name="maxSteps">The longest trace to generate.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    public static SpecReport Sample<S>(this Spec<S> spec, Action<string>? writeLine = null, int minSteps = 1, int maxSteps = 24,
        string? seed = null, long iter = -1, int time = -1, int threads = -1)
    {
        // ArgPairs is built by Validate, and every other engine reaches it through the walk it starts.
        spec.Validate();
        var counters = new SpecCounters(spec.Requirements.Count, spec.ArgPairs);
        var report = SpecReportOf(spec, $"Spec.Sample of {Plural(spec.Requirements.Count, "requirement")}", counters);
        try
        {
            spec.GenTrace(minSteps, maxSteps).Sample(
                trace => SpecWalk(spec, trace, counters) is null,
                null, seed, iter, time, threads,
                trace => SpecCheck(spec, trace, null)?.ToString(spec.Printer) ?? trace.ToString(spec.Printer, -1));
        }
        finally
        {
            report.TracesWalked = counters.Traces;
            report.StepsWalked = counters.Steps;
            writeLine?.Invoke(report.ToString());
        }
        return report;
    }

    /// <summary>Enumerate the whole reachable state space breadth first, checking every requirement on every transition.
    /// Outstanding <c>Response</c> deadlines and <c>Precedes</c> history are part of the
    /// search state, so when the space closes every requirement is proved for the model, not sampled. Any violation is
    /// reported with a shortest path to it.</summary>
    /// <param name="spec">The specification to explore.</param>
    /// <param name="writeLine">WriteLine function for the proof certificate.</param>
    /// <param name="maxStates">Give up after this many distinct states (default 10,000,000, measured at 2.0GB peak and
    /// under four seconds when actually reached). Hitting this proves nothing; declare a <c>Boundary</c> instead and the
    /// exploration closes over a region you chose. The boundary applies here and to <c>Faults</c>, not to <c>Sample</c>
    /// or <c>Conform</c>, whose walks are already bounded by their step count and so cannot fail to terminate. Raising
    /// it much further is not free: the cost is linear in states, so ten times this is twenty gigabytes, and the point
    /// of the limit is to turn an unbounded model into a report rather than into an out of memory.</param>
    /// <param name="maxDepth">Stop after this many steps from the initial state. Like <paramref name="maxStates"/> and
    /// unlike <c>Boundary</c> this truncates rather than scopes, so the report is not closed and proves nothing.</param>
    /// <param name="threads">Threads to expand each frontier level on, default 1. Results do not depend on it:
    /// expansion is parallel but insertion is sequential in source order, so the state count, the counterexample
    /// chosen among several at the same depth, and every coverage number are the same on one thread as on many. That
    /// holds for a failing run too: a violation stops the inserting, but the node it was found in is evaluated to the
    /// end either way, because the parallel path expands a whole node before inserting any of it.
    /// It is opt in because the sequential visited set bounds the speedup, so it only pays when guards, transitions
    /// and requirement predicates are expensive - measured on 22 cores at about 2x for costly delegates and about 0.8x
    /// for free ones, in Tests/SpecScaleTests. Cheap delegates are the reason it is off by default: buffering a level
    /// and handing it out costs more than it saves, so this is a loss rather than a wash. Above one thread the delegates
    /// must also be thread safe, not merely pure.</param>
    /// <param name="throwOnViolation">Throw a <see cref="CsCheckException"/> on the first violation (default true).</param>
    public static SpecReport Exhaustive<S>(this Spec<S> spec, Action<string>? writeLine = null, int maxStates = 10_000_000,
        int maxDepth = int.MaxValue, int threads = 1, bool throwOnViolation = true)
        => Exhaustive(spec, null, writeLine, maxStates, maxDepth, threads, throwOnViolation, out _);

    /// <summary>Enumerate the whole reachable state space breadth first, returning any violation with a shortest
    /// path to it instead of throwing. Use this to assert that a requirement is genuinely falsifiable.</summary>
    /// <param name="spec">The specification to explore.</param>
    /// <param name="violation">The shortest-path violation, or null when nothing failed.</param>
    /// <param name="writeLine">WriteLine function for the proof certificate.</param>
    /// <param name="maxStates">Give up after this many distinct states (default 10,000,000).</param>
    /// <param name="maxDepth">Stop after this many steps from the initial state.</param>
    /// <param name="threads">Threads to expand each frontier level on, default 1. The result does not depend on it.</param>
    public static SpecReport Exhaustive<S>(this Spec<S> spec, out SpecViolation<S>? violation,
        Action<string>? writeLine = null, int maxStates = 10_000_000, int maxDepth = int.MaxValue, int threads = 1)
        => Exhaustive(spec, null, writeLine, maxStates, maxDepth, threads, false, out violation);

    static SpecReport Exhaustive<S>(Spec<S> spec, SpecFault<S>? fault, Action<string>? writeLine, int maxStates,
        int maxDepth, int threads, bool throwOnViolation, out SpecViolation<S>? violation)
    {
        spec.Validate();
        // The give-up test is an equality against a count that starts at one, so a non-positive limit would never
        // match and an unbounded model would run to exhaustion rather than reporting that it gave up.
        if (maxStates < 1) ThrowHelper.Throw($"Spec Exhaustive maxStates must be at least 1, was {maxStates}");
        if (maxDepth < 0) ThrowHelper.Throw($"Spec Exhaustive maxDepth cannot be negative, was {maxDepth}");
        var counters = new SpecCounters(spec.Requirements.Count, spec.ArgPairs);
        var report = SpecReportOf(spec, fault is null ? $"Spec.Exhaustive of {Plural(spec.Requirements.Count, "requirement")}"
                                                      : $"Spec.Exhaustive with fault '{fault.Name}'", counters);
        violation = null;
        var detail = SpecInitial(spec, counters, out var ri);
        if (detail is not null)
        {
            violation = new SpecViolation<S>(spec.Requirements[ri].Id, spec.Requirements[ri].Quote, detail, -1,
                new Trace<S>(spec.Initial, [], false));
            // Every other failing path reports before it returns or throws; this one was silent.
            writeLine?.Invoke(report.ToString());
            if (throwOnViolation) throw new CsCheckException(violation.ToString(spec.Printer));
            return report;
        }
        // The frontier owns the visited set, which is a set and not a map: nothing ever looked a node up by index, and
        // Add doubling as the membership test is what keeps a new state to one hash of the whole node rather than two.
        var walk = new SpecFrontier<S>(spec, fault, report, counters, maxStates, maxDepth);
        if (threads < 1) threads = 1;
        if (threads == 1) walk.Sequential();
        else walk.Parallel(threads);

        report.Revisits = walk.Revisits;
        // Computed here rather than in each branch below, so it is there whatever the run's outcome.
        if (report.DeadlockStates != 0) report.DeadlockTrace = walk.DeadlockPath()?.ToString(spec.Printer, -1);
        violation = walk.Found;
        if (walk.Found is not null)
        {
            if (writeLine is not null) writeLine(report.ToString());
            if (throwOnViolation) throw new CsCheckException(walk.Found.ToString(spec.Printer));
            return report;
        }
        if (walk.GaveUp)
        {
            var widest = WidestFields(spec, walk.Nodes);
            report.Note = $"gave up at {maxStates:#,0} states - "
                + (widest is null ? "abstract the model further"
                                  : $"widest state fields are {widest}; saturate or bound the widest")
                + ", or drop from the state's Equals and GetHashCode whatever the behaviour never reads"
                + ", or raise maxStates knowing it costs roughly 200 bytes per state for a narrow state and half again "
                + "for a wide one"
                // Only worth saying when nothing was revisited, and even then as a check rather than a diagnosis: an
                // honestly infinite model never revisits either.
                + (walk.Revisits == 0 ? "; no state was ever revisited, so check as well that no field of the state breaks "
                                      + "its value equality" : "");
            writeLine?.Invoke(report.ToString());
            return report;
        }
        // maxDepth truncates like maxStates, not like Boundary: the states past it are inside whatever region was
        // asked for and simply were not looked at, so nothing may be concluded and the space has not closed.
        if (walk.Truncated)
        {
            report.States = walk.States;
            report.Note = $"stopped at maxDepth {maxDepth} - states beyond it were not explored, so nothing is proved";
            writeLine?.Invoke(report.ToString());
            return report;
        }
        report.States = walk.States;
        report.Closed = true;
        // Worded as an observation, not a diagnosis: a model that only ever advances is legitimately a tree, so a
        // correct spec must not be told its value equality is broken.
        if (walk.Revisits == 0 && report.States > 8)
            report.Note = "no state was ever revisited, so the reachable space is a tree - expected if the model only "
                + "advances, otherwise a field of the state is breaking value equality";
        // The space closed, so a Reachable requirement that never held is not merely unobserved, it is unreachable -
        // unless states were pruned, in which case it may hold only outside the boundary and nothing can be concluded.
        var unreachable = -1;
        List<string>? unheld = null;
        for (int i = 0; i < spec.Requirements.Count; i++)
        {
            if (spec.Requirements[i].Kind != ReqKind.Reachable || counters.Triggered[i] != 0) continue;
            if (unreachable < 0) unreachable = i;
            (unheld ??= []).Add(spec.Requirements[i].Id);
        }
        if (unheld is not null)
        {
            if (report.Pruned == 0)
            {
                var req = spec.Requirements[unreachable];
                violation = new SpecViolation<S>(req.Id, req.Quote,
                    "is unreachable: the state space closed without it ever holding", -1, new Trace<S>(spec.Initial, [], false));
                writeLine?.Invoke(report.ToString());
                if (throwOnViolation) throw new CsCheckException(violation.ToString(spec.Printer));
                return report;
            }
            var note = "never held, but states outside the boundary were not explored so this is not a failure: "
                + string.Join(", ", unheld);
            report.Note = report.Note is null ? note : $"{report.Note}; {note}";
        }
        writeLine?.Invoke(report.ToString());
        return report;
    }

    /// <summary>The reachable state graph in Graphviz DOT, for a model small enough to look at. Terminal states are
    /// drawn doubled and states with nothing enabled that were not declared <c>Terminal</c> are filled, so a dead end
    /// is visible without reading anything. Pipe it through <c>dot -Tsvg</c>.</summary>
    /// <remarks>This walks the space itself rather than reusing <c>Exhaustive</c>, because the walk keeps only a
    /// spanning tree of parent links - enough to rebuild one path, but not the graph. Requirements are not evaluated:
    /// the picture is for understanding a model, and <c>Exhaustive</c> is for proving things about it.</remarks>
    /// <param name="spec">The specification to draw.</param>
    /// <param name="maxStates">Give up after this many states, since a picture stops being useful long before a proof
    /// does (default 200).</param>
    public static string Dot<S>(this Spec<S> spec, int maxStates = 200)
    {
        spec.Validate();
        // Keyed by SpecNode rather than S, which is unconstrained and so cannot key a Dictionary. The deadline and
        // history words stay zero here because no requirement is evaluated, so this is a plain state key.
        var ids = new Dictionary<SpecNode<S>, int> { [new(spec.Initial, 0UL, 0UL, 0UL)] = 0 };
        var states = new List<S> { spec.Initial };
        var sb = new StringBuilder("digraph spec {\n  rankdir=LR;\n  node [shape=box, fontname=\"monospace\"];\n");
        var truncated = false;
        // Every discovered state is expanded, because a state the loop stopped short of would still be pointed at by
        // the edge that discovered it and Graphviz would draw it captioned with its node id. The cap bounds states, so
        // this still terminates; what the cap skips is an edge needing a state past it, not a state's own label.
        for (int head = 0; head < states.Count; head++)
        {
            var state = states[head];
            var enabled = 0;
            var cutOff = false;
            for (int a = 0; a < spec.Actions.Count; a++)
            {
                var action = spec.Actions[a];
                for (int g = 0; g < action.ArgCount; g++)
                {
                    if (!action.Enabled(state, g)) continue;
                    enabled++;
                    var after = action.Apply(state, g);
                    if (!ids.TryGetValue(new(after, 0UL, 0UL, 0UL), out var to))
                    {
                        if (states.Count == maxStates) { cutOff = truncated = true; continue; }
                        to = states.Count;
                        ids.Add(new(after, 0UL, 0UL, 0UL), to);
                        states.Add(after);
                    }
                    var arg = action.ArgName(g);
                    sb.Append("  n").Append(head).Append(" -> n").Append(to).Append(" [label=\"")
                      .Append(Escape(arg.Length == 0 ? action.Name : $"{action.Name}({arg})"))
                      .Append("\"];\n");
                }
            }
            // Dashed says the drawing stops here, which is not the same as the model stopping here - without it a state
            // whose successors were all dropped looks exactly like an intended end.
            var shape = cutOff ? ", style=dashed"
                      : enabled != 0 ? ""
                      : spec.IsTerminal?.Invoke(state) == true ? ", shape=doublecircle"
                      : ", style=filled, fillcolor=\"#ffcccc\"";
            sb.Append("  n").Append(head).Append(" [label=\"").Append(Escape(spec.Printer(state)))
              .Append('"').Append(shape).Append("];\n");
        }
        // On whether an edge was actually dropped, not on reaching the cap, so a model of exactly maxStates states that
        // was drawn in full is not labelled as given up on.
        if (truncated) sb.Append("  truncated [label=\"gave up at ").Append(maxStates)
            .Append(" states\", shape=plaintext];\n");
        return sb.Append("}\n").ToString();

        static string Escape(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal)
                                           .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    // Which state fields have the most distinct values, sampled from the states already reached. This is the
    // answer to "why did it not close": the widest field is the one to saturate or bound.
    // Reflection free, so it reads the printed state rather than the type. A record's generated ToString is
    // "Name { A = 1, B = 2 }", which is the shape the documented requirement on S already implies, and the
    // split tracks brace depth so a nested record counts as one value of its own field. A state with a hand written
    // ToString will not parse, and then this returns null rather than a guess. Only called when a run gives up, so
    // the hot path pays nothing for it.
    static string? WidestFields<S>(Spec<S> spec, List<SpecNode<S>> nodes)
    {
        var names = new List<string>();
        var distinct = new List<HashSet<string>>();
        var pairs = new List<(string Name, string Value)>();
        var step = Math.Max(1, nodes.Count / 5000);
        for (int i = 0; i < nodes.Count; i += step)
        {
            if (!ParseFields(spec.Printer(nodes[i].State), pairs)) return null;
            if (names.Count == 0)
                foreach (var (name, _) in pairs) { names.Add(name); distinct.Add([with(StringComparer.Ordinal)]); }
            else if (pairs.Count != names.Count) return null;
            for (int f = 0; f < pairs.Count; f++)
            {
                if (!string.Equals(names[f], pairs[f].Name, StringComparison.Ordinal)) return null;
                distinct[f].Add(pairs[f].Value);
            }
        }
        if (names.Count == 0) return null;
        var order = new int[names.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (x, y) => distinct[y].Count.CompareTo(distinct[x].Count));
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Min(3, order.Length); i++)
        {
            var n = distinct[order[i]].Count;
            if (i != 0) sb.Append(", ");
            sb.Append(names[order[i]]).Append(" (").Append(n).Append(n == 1 ? " value)" : " values)");
        }
        return sb.ToString();
    }

    // Splits "Name { A = 1, B = Node { C = 2 } }" into its top level Name = Value pairs, so a nested record
    // is one value rather than several fields. False when the text is not that shape.
    static bool ParseFields(string text, List<(string, string)> pairs)
    {
        pairs.Clear();
        var open = text.IndexOf('{', StringComparison.Ordinal);
        var close = text.LastIndexOf('}');
        if (open < 0 || close <= open) return false;
        var inner = text[(open + 1)..close];
        int depth = 0, start = 0, eq = -1;
        for (int c = 0; c <= inner.Length; c++)
        {
            if (c == inner.Length || (depth == 0 && inner[c] == ','))
            {
                if (eq < 0) return false;
                pairs.Add((inner[start..eq].Trim(), inner[(eq + 1)..c].Trim()));
                start = c + 1;
                eq = -1;
                continue;
            }
            var ch = inner[c];
            if (ch == '{') depth++;
            else if (ch == '}') { if (--depth < 0) return false; }
            else if (depth == 0 && ch == '=' && eq < 0) eq = c;
        }
        return depth == 0 && pairs.Count != 0;
    }

    /// <summary>Mutation testing for the specification itself. Each declared <c>Fault</c> is injected in
    /// turn and the state space re-explored. A fault that no requirement catches means a requirement is missing; a
    /// requirement that catches no fault is a candidate for being too weak.</summary>
    /// <remarks>The <c>Caught by</c> column is the part to assert on rather than merely print: a fault caught by a
    /// different requirement than intended passes while leaving the intended one unproven, and that has happened twice
    /// in these examples. Use <see cref="SpecFaultsReport.CaughtBy"/> for that.</remarks>
    /// <param name="spec">The specification to mutate.</param>
    /// <param name="writeLine">WriteLine function for the fault table.</param>
    /// <param name="maxStates">Give up after this many distinct states per fault (default 10,000,000).</param>
    /// <param name="maxDepth">Stop after this many steps from the initial state.</param>
    /// <param name="threads">Threads to expand each frontier level on, default 1.</param>
    /// <param name="throwOnUncaught">Throw a <see cref="CsCheckException"/> when a fault went undetected (default true).</param>
    public static SpecFaultsReport Faults<S>(this Spec<S> spec, Action<string>? writeLine = null, int maxStates = 10_000_000,
        int maxDepth = int.MaxValue, int threads = 1, bool throwOnUncaught = true)
        => FaultsReport(spec, $"Spec.Faults over {Plural(spec.FaultList.Count, "fault")}",
            spec.InBoundary is null ? null
                : "within the declared boundary: a fault caught by NOTHING may still be caught outside it",
            "No requirement detects these faults", writeLine, throwOnUncaught,
            baseline: () => { Exhaustive(spec, null, null, maxStates, maxDepth, threads, true, out _); },
            fault => { var r = Exhaustive(spec, fault, null, maxStates, maxDepth, threads, false, out var v); return (v, r.Closed); });

    /// <summary>Mutation testing for a specification whose state space is too large to close. Each declared
    /// <c>Fault</c> is injected in turn and the specification walked randomly, and the shallowest violation found is
    /// reported. <c>Faults</c> is strictly better where it can run - it proves a fault is undetectable rather than
    /// failing to find it - so reach for this only when <c>Exhaustive</c> gives up.</summary>
    /// <remarks>Two columns mean less here than in the proved table. <c>Steps</c> is the shallowest counterexample
    /// sampled rather than the shallowest that exists, and <c>NOTHING</c> means no requirement was seen to detect the
    /// fault rather than that none can. A <c>Reachable</c> requirement can never appear in <c>Caught by</c> at all,
    /// because unreachability only follows from closure.
    /// <para>Before injecting any fault, the unmutated spec is walked over the same budget and rejected immediately
    /// if a requirement already fails. The baseline uses sampling rather than <c>Exhaustive</c> so the cost is
    /// proportional: one extra fault-free pass, not a full exhaustive search on a space that does not close.</para>
    /// <para>The budget is per fault, so the total work is <paramref name="iter"/> walks times the number of faults
    /// plus one baseline pass.</para></remarks>
    /// <param name="spec">The specification to mutate.</param>
    /// <param name="writeLine">WriteLine function for the fault table.</param>
    /// <param name="minSteps">The shortest trace to generate.</param>
    /// <param name="maxSteps">The longest trace to generate.</param>
    /// <param name="seed">The initial seed to use for the first iteration of each fault.</param>
    /// <param name="iter">The number of walks per fault (default 100).</param>
    /// <param name="time">The number of seconds to run per fault.</param>
    /// <param name="threads">The number of threads to walk on (default number logical CPUs).</param>
    /// <param name="throwOnUncaught">Throw a <see cref="CsCheckException"/> when a fault went undetected (default true).</param>
    public static SpecFaultsReport SampleFaults<S>(this Spec<S> spec, Action<string>? writeLine = null, int minSteps = 1,
        int maxSteps = 24, string? seed = null, long iter = -1, int time = -1, int threads = -1, bool throwOnUncaught = true)
        => FaultsReport(spec, $"Spec.SampleFaults over {Plural(spec.FaultList.Count, "fault")}",
            "sampled, so Steps is the shallowest counterexample found and NOTHING means none was found, not that none exists",
            "No requirement detected these faults in the walks sampled", writeLine, throwOnUncaught,
            // Sampled baseline: walk the unmutated spec over the same budget rather than running Exhaustive (which
            // would give up at maxStates on a space that doesn't close — exactly why SampleFaults was chosen).
            // This covers exactly the traces the fault walks will later sample, so any base violation reachable by
            // sampling is caught here too, and the cost is one extra fault-free pass rather than a 10M-state search.
            baseline: () => { var v = SampleFault(spec, new SpecFault<S>("(baseline)", (_, _) => false, (_, a) => a),
                                                   minSteps, maxSteps, seed, iter, time, threads);
                               if (v is not null) throw new CsCheckException(v.ToString(spec.Printer)); },
            // Sampling always concludes (the budget runs out, never "gives up"), so closed is always true.
            fault => (SampleFault(spec, fault, minSteps, maxSteps, seed, iter, time, threads), true));

    // Walk one fault, keeping the violation that happened on the earliest step of any trace.
    // Ranked by the step the violation happened on rather than by the length of the trace that reached it,
    // because that is the number the proved table reports and the trace can be cut back to exactly that prefix. It
    // also means shrinking would add nothing: a violation at step index n already has a minimal length path in front
    // of it, and the sampling budget is better spent finding a shallower one than simplifying this one.
    static SpecViolation<S>? SampleFault<S>(Spec<S> spec, SpecFault<S> fault, int minSteps, int maxSteps, string? seed,
        long iter, int time, int threads)
    {
        var gate = new object();
        SpecViolation<S>? best = null;
        var bestStep = int.MaxValue;
        new GenSpecTrace<S>(spec, minSteps, maxSteps, fault).Sample(trace =>
        {
            var violation = SpecCheck(spec, trace, null);
            // The unlocked read is a filter only, and an int read cannot tear, so a stale one costs at most a lock.
            if (violation is not null && violation.StepIndex < bestStep)
            {
                lock (gate)
                {
                    if (violation.StepIndex < bestStep) { bestStep = violation.StepIndex; best = violation; }
                }
            }
        }, null, seed, iter, time, threads);
        if (best is null) return null;
        var steps = best.Trace.Steps;
        if (steps.Length == best.StepIndex + 1) return best;
        var cut = new Transition<S>[best.StepIndex + 1];
        Array.Copy(steps, cut, cut.Length);
        return new SpecViolation<S>(best.Id, best.Quote, best.Detail, best.StepIndex,
            new Trace<S>(best.Trace.Initial, cut, false));
    }

    static SpecFaultsReport FaultsReport<S>(Spec<S> spec, string mode, string? caveat, string uncaughtMessage,
        Action<string>? writeLine, bool throwOnUncaught, Action baseline,
        Func<SpecFault<S>, (SpecViolation<S>? Violation, bool Closed)> run)
    {
        // Every other engine validates through the walk it starts. This one would skip it entirely for a spec with no
        // faults declared, so a typo in an on: name would go unreported.
        spec.Validate();
        // A spec that already violates a requirement without any fault injected will report every mutation as "caught",
        // because the base violation is found regardless. Fail immediately so the table is not filled with misleading
        // "caught" entries from a spec that was never correct. The baseline is supplied by the caller: Faults uses
        // Exhaustive (a real proof), SampleFaults uses a sampled walk over the same budget (cost-proportional and
        // checks exactly the traces that the fault walks will later sample).
        baseline();
        var w = 5;
        for (int i = 0; i < spec.FaultList.Count; i++) if (spec.FaultList[i].Name.Length > w) w = spec.FaultList[i].Name.Length;
        // Measured, so an id of any length still lines the table up.
        var c = 9;
        for (int i = 0; i < spec.Requirements.Count; i++) if (spec.Requirements[i].Id.Length > c) c = spec.Requirements[i].Id.Length;
        var sb = new StringBuilder(mode)
            .Append("\n  | ").Append("Fault".PadRight(w)).Append(" | ").Append("Caught by".PadRight(c)).Append(" | Steps |");
        var results = new SpecFaultResult[spec.FaultList.Count];
        var uncaught = new List<string>();
        var inconclusive = new List<string>();
        var caught = new HashSet<string>(StringComparer.Ordinal);
        for (int f = 0; f < spec.FaultList.Count; f++)
        {
            var fault = spec.FaultList[f];
            var (violation, closed) = run(fault);
            var outcome = violation is not null ? FaultOutcome.Caught
                        : closed ? FaultOutcome.NotDetected
                        : FaultOutcome.Inconclusive;
            if (outcome == FaultOutcome.Caught) caught.Add(violation!.Id);
            else if (outcome == FaultOutcome.NotDetected) uncaught.Add(fault.Name);
            else inconclusive.Add(fault.Name);
            results[f] = new SpecFaultResult(fault.Name, outcome, violation?.Id, violation?.Trace.Steps.Length ?? 0);
            var label = outcome == FaultOutcome.Caught ? violation!.Id
                      : outcome == FaultOutcome.NotDetected ? "NOTHING" : "NOT CLOSED";
            sb.Append("\n  | ").Append(fault.Name.PadRight(w)).Append(" | ")
              .Append(label.PadRight(c)).Append(" | ")
              .Append((violation is null ? "" : (violation.Trace.Steps.Length).ToString()).PadLeft(5)).Append(" |");
        }
        var idle = new List<string>();
        foreach (var r in spec.Requirements)
            // Reachable requirements are excluded. A fault can break one, but only by making a state unreachable, and a
            // fault written to do that says nothing about whether a requirement is strong enough.
            if (r.Kind != ReqKind.Reachable && !caught.Contains(r.Id)) idle.Add(r.Id);
        if (idle.Count != 0)
            sb.Append("\n  no declared fault exercises: ").AppendJoin(", ", idle);
        if (caveat is not null) sb.Append("\n  ").Append(caveat);
        if (inconclusive.Count != 0)
            sb.Append("\n  inconclusive (search did not close): ").AppendJoin(", ", inconclusive);
        var report = new SpecFaultsReport(results, uncaught, inconclusive, idle, sb.ToString());
        writeLine?.Invoke(report.ToString());
        if (uncaught.Count != 0 && throwOnUncaught)
            throw new CsCheckException($"{uncaughtMessage}: {string.Join(", ", uncaught)}");
        return report;
    }

    /// <summary>Check a real implementation conforms to the specification on sampled traces. The same random walk
    /// drives both; after every step <paramref name="apply"/> performs the action on the implementation and returns
    /// whether what it did agrees with the model state the specification reached. Any disagreement shrinks to the
    /// shortest trace. This is testing, not refinement: <paramref name="apply"/> compares whichever fields you choose,
    /// over the traces generated, so a clean run means no counterexample was found rather than none exists.</summary>
    /// <param name="spec">The specification to explore.</param>
    /// <param name="create">Creates a fresh implementation for each trace.</param>
    /// <param name="apply">Performs one step on the implementation, returning false when it disagrees with the model.</param>
    /// <param name="writeLine">WriteLine function for the coverage report.</param>
    /// <param name="minSteps">The shortest trace to generate.</param>
    /// <param name="maxSteps">The longest trace to generate.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    public static SpecReport Conform<S, TSut>(this Spec<S> spec, Func<TSut> create, Func<TSut, Transition<S>, bool> apply,
        Action<string>? writeLine = null, int minSteps = 1, int maxSteps = 24, string? seed = null, long iter = -1,
        int time = -1, int threads = -1)
    {
        spec.Validate();
        var counters = new SpecCounters(spec.Requirements.Count, spec.ArgPairs);
        var report = SpecReportOf(spec,
            $"Spec.Conform of {typeof(TSut).Name} to {Plural(spec.Requirements.Count, "requirement")}", counters);
        static int Diverged<S2, TSut2>(Func<TSut2> create, Func<TSut2, Transition<S2>, bool> apply, Trace<S2> trace)
        {
            var sut = create();
            for (int i = 0; i < trace.Steps.Length; i++)
                if (!apply(sut, trace.Steps[i])) return i;
            return -1;
        }
        try
        {
            spec.GenTrace(minSteps, maxSteps).Sample(
                trace => SpecWalk(spec, trace, counters) is null && Diverged(create, apply, trace) == -1,
                null, seed, iter, time, threads,
            trace =>
            {
                var violation = SpecCheck(spec, trace, null);
                if (violation is not null) return violation.ToString(spec.Printer);
                // Re-running Diverged here to find the step for the error message. If apply throws deterministically
                // the printer would throw too and Sample could not produce the shrunk CsCheckException with the trace.
                int i;
                try { i = Diverged(create, apply, trace); }
                catch (Exception e) { i = -1; return $"\n  Implementation threw during conformance check: {e.Message}"; }
                return new StringBuilder("\n  Implementation diverged from the specification at step ").Append(i + 1)
                    .Append("\n         Trace: ").Append(trace.ToString(spec.Printer, i)).ToString();
            });
        }
        finally
        {
            report.TracesWalked = counters.Traces;
            report.StepsWalked = counters.Steps;
            writeLine?.Invoke(report.ToString());
        }
        return report;
    }
}
