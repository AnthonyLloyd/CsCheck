# cscheck

Agent skills for [CsCheck](https://github.com/AnthonyLloyd/CsCheck). Each `SKILL.md` follows the [agentskills.io](https://agentskills.io) standard, so the skills load in any agent that reads it. They were built and tested in Claude Code. The install steps for the other agents follow their documentation.

## Skills

- [writing-cscheck-tests](skills/writing-cscheck-tests/SKILL.md) writes or strengthens tests, preferring CsCheck properties when appropriate, falling back to normal tests for specific bugs. It covers `Sample`, `Equality`, `SampleModelBased`, `SampleMetamorphic`, `SampleParallel`, `Spec`, `Faster`, causal profiling, `Single` and `Check.Hash`. It also covers generators that give usable inputs and hit the edges, readable shrunk failures, how long to run before trusting a pass, when a property can replace example tests, and what changed in CsCheck 5.
- [finding-test-gaps](skills/finding-test-gaps/SKILL.md) finds the weak spots that coverage and mutants reveal. Code coverage shows code no test runs, and a sweep of hand-picked mutants shows code the tests run but don't check. Each mutant is compiled and tested in a copy, so no source file in the repo changes. It reports the gaps, and writes the tests they need when asked.
- [closing-test-gaps](skills/closing-test-gaps/SKILL.md) hunts for the bugs behind the gaps by reading code against its siblings, docs, callers and history, and probing the edges of what it must support. It confirms each candidate with a probe or a CsCheck property, and ranks them by how likely each is to be real and to be live.

Together they make a loop: finding-test-gaps shows what the tests miss, closing-test-gaps finds the bugs behind it, writing-cscheck-tests writes the properties that close the gaps, and finding-test-gaps checks the new properties aren't hollow.

## Requirements

- **writing-cscheck-tests:** CsCheck 5, which targets net10.0 and net11.0. A project on an older framework needs CsCheck 4.9.1, whose differences the skill lists.
- **finding-test-gaps:**
  - The .NET 10 SDK or later, since its tools are C# file-based apps. The first run of `mutants.cs` or `candidates.cs` restores Roslyn from nuget.org, or the feed your NuGet.config names.
  - SDK-style C# test projects on VSTest or Microsoft.Testing.Platform, with NUnit, xUnit, MSTest or TUnit, and one or more target frameworks. A Microsoft.Testing.Platform project needs the `Microsoft.Testing.Extensions.CodeCoverage` and `Microsoft.Testing.Extensions.TrxReport` packages, which TUnit and MSTest's `MSTest` package bring.
  - git, to compare with a branch. Without it, `uncovered.cs` also leaves out the use counts, the callers and the projects with no tests of their own.
  - Windows, Linux or macOS, which the Skills workflow checks.
- **closing-test-gaps:** the .NET 10 SDK, for its probes, small programs that confirm a bug.

## What it costs, and what it changes

- Coverage is one run of the tests with coverage on.
- A mutant sweep runs each mutant against the file's own tests, four at a time: 3 to 7 minutes for a file in the trials. A whole session that measured, wrote the tests and checked them took an agent 25 to 45 minutes. finding-test-gaps asks before sweeping more than a few hundred mutants.
- Nothing in the repo changes except the tests you ask for, and the `.received.` files a snapshot test writes when a mutant breaks it. The mutants' tests run in copies of the test output in `bin`, which are deleted at the end. writing-cscheck-tests asks before adding the CsCheck package to a project that doesn't use it.

## Install

### Claude Code

```
/plugin marketplace add AnthonyLloyd/CsCheck
/plugin install cscheck@cscheck
```

Run `/reload-plugins` if it asks, then type `/cscheck:` to see the skills. To update, run `/plugin marketplace update cscheck`, or turn on auto-update for the marketplace in `/plugin`.

### GitHub Copilot CLI

```
/plugin marketplace add AnthonyLloyd/CsCheck
/plugin install cscheck@cscheck
```

`copilot plugin update cscheck` updates it.

### VS Code

Add this repository to the `chat.plugins.marketplaces` setting. A list in settings.json replaces the default one, so keep the marketplaces already in it, such as VS Code's default:

```jsonc
// settings.json
{
  "chat.plugins.marketplaces": ["github/awesome-copilot#marketplace", "AnthonyLloyd/CsCheck"]
}
```

With `chat.plugins.enabled` on, as it is by default, run **Chat: Open Customizations** from the Command Palette, select **Browse**, and install cscheck from this marketplace.

### Cursor

Copy the `plugins/cscheck` folder of this repository to `~/.cursor/plugins/local/cscheck`, then restart Cursor or run **Developer: Reload Window**. Copy it rather than linking to it, since Cursor skips a symlink to a folder outside `~/.cursor/plugins/local`.

### Codex CLI

```
codex plugin marketplace add AnthonyLloyd/CsCheck
```

Then open `/plugins` and install cscheck from this marketplace. `codex plugin marketplace upgrade cscheck` updates it.

### Other agents

Copy the folders in `plugins/cscheck/skills/` to the folder your agent reads skills from.

## Where the rules come from

From published research:
- **Reading a member's tests before writing its mutants:** on Python benchmark functions, LLMs shown the tests wrote mutants that slipped past them yet were real faults 79-88% of the time, against 12-23% without them (Kiele et al., "Beyond Rule-Based Mutation Testing: Test-Aware Mutant Generation Using Large Language Models", ESEM 2026, [arXiv:2609.35841](https://arxiv.org/abs/2609.35841)).
- **Flagging mutants that change nothing, and judging equivalence from the code, not its comments:** at Meta, comments the mutant generator had added misled an LLM judging which mutants were equivalent. Dropping mutants identical to the original and stripping those comments first raised its precision from 0.79 to 0.95 and its recall from 0.47 to 0.96 (Foster et al., "Mutation-Guided LLM-based Test Generation at Meta", FSE 2025, [arXiv:2501.12862](https://arxiv.org/abs/2501.12862)). `mutants.cs` warns about a mutant that only changes whitespace or comments.
- **Skipping logging, metrics, tracing and sleeps:** Google's mutation testing calls such code arid and skips it, along with debugging code such as asserts (Petrović et al., "Practical Mutation Testing at Scale: A view from Google", IEEE TSE 48(10), 2022, [arXiv:2102.11378](https://arxiv.org/abs/2102.11378)).

From our own trials, run by the skills' author on a private codebase and on CsCheck itself, whose reports aren't published:
- **Sweeping every member:** writing mutants only where a recipe seemed to fit skipped whole members, one of them with about 50 call sites. A sweep of every member then found 25 of the 28 gaps a blind judge rated worth a test, against 17 for Stryker.NET. It ran on the three files the rules were drawn from, so this shows the rules carry over, not that they generalise.
- **Reporting gaps, not a score:** Stryker.NET scored one file 87.7%, counting timeouts as kills, and 58% without them. So finding-test-gaps reports the gaps that matter, and treats a timeout as unsettled.
- **Why measuring and hunting are separate skills:** in a blind trial on CsCheck itself, with three past bugs planted, Claude Opus found all three both with an earlier version of these skills and without them. Without them it was faster and found two more bugs already in the code; with them, its tests killed 75 of its own 88 mutants, against 61 for the other run's tests. So the bug hunt became closing-test-gaps, which leads with reading the code and probing its edges, and finding-test-gaps measures.

## Your own notes

The skills are generic. To add your codebase's conventions, such as its test framework's output call, its scratch folder or its own common kinds of bug, put them in a file and point your agent's instructions at it (`CLAUDE.md`, `AGENTS.md` or `.github/copilot-instructions.md`), for example "When using the cscheck skills here, also read `docs/testing-notes.md`".
