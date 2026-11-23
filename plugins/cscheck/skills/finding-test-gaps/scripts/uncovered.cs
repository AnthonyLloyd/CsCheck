#!/usr/bin/env dotnet
#:property PublishAot=false
#:property Optimize=true
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property RunAnalyzers=false
#:property LangVersion=latest

// Lists what no test ran in a cobertura coverage report.
// Named files: their lines not run, branches partly taken, and their members in source order, with the uses of each name and the lines each didn't run,
// and with --callers some of each member's uses outside tests and in them, with their arguments. --sweep <file> does that too, and starts
// the sweep's table in a new file: a row per member, with the columns for its mutants from the list and by hand, its property and what
// was skipped left to fill in.
// --since <ref>: the lines changed since the merge base with <ref> (uncommitted and untracked included) that no test ran, and the members they're in, in the named files if any.
// Neither: the source files with the most lines not run, in the projects under test unless --all, then the projects with no test project of their own.
// The #:property lines stop a repo's Directory.Build.props from changing how this builds, when the skill is copied into a repo.
// dotnet run --file uncovered.cs -- <results folder or .cobertura.xml> [file or type names, or partial paths, space or comma separated] [--callers] [--sweep <file>] [--since <ref>] [--top 20] [--all]

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string? path = null, since = null, sweepFile = null;
var top = 20;
var all = false;
var callers = false;
var names = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--since") since = ++i < args.Length ? args[i] : "";
    else if (args[i] == "--top") top = ++i < args.Length && int.TryParse(args[i], out var t) && t > 0 ? t : 0;
    else if (args[i] == "--all") all = true;
    else if (args[i] == "--callers") callers = true;
    else if (args[i] == "--sweep") sweepFile = ++i < args.Length ? args[i] : "";
    else if (args[i].StartsWith("--", StringComparison.Ordinal)) return Usage();
    else if (path is null) path = args[i];
    else names.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
if (path is null || since == "" || top == 0 || sweepFile == "") return Usage();
callers |= sweepFile is not null;
if (callers && (names.Count == 0 || since is not null))
{
    Console.Error.WriteLine("error: --callers and --sweep are for the named files' members, so they need file or type names, and don't go with --since");
    return 2;
}
// The agent fills the table in as it sweeps, so a second run mustn't wipe that out.
if (sweepFile is not null && Path.Exists(sweepFile))
{
    Console.Error.WriteLine($"error: {sweepFile} already exists, so it wasn't overwritten: name a new file, or delete it to start the table again");
    return 1;
}
if (!Path.Exists(path))
{
    Console.Error.WriteLine($"error: {path} doesn't exist: pass the --results-directory folder or a .cobertura.xml file");
    return 1;
}
// A solution's coverage gives one report per test project, so every report under the folder is merged.
var reports = Directory.Exists(path)
    ? new DirectoryInfo(path).EnumerateFiles("*.cobertura.xml", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc).ToArray()
    : [new FileInfo(path)];
if (reports.Length == 0)
{
    Console.Error.WriteLine($"error: no .cobertura.xml under {path}");
    return 1;
}
var report = reports.Length == 1 ? reports[0].FullName : $"the {reports.Length} reports under {path}";
if (reports.Length > 1) Console.WriteLine($"Merged {reports.Length} reports, written {reports[0].LastWriteTime:yyyy-MM-dd HH:mm} to {reports[^1].LastWriteTime:yyyy-MM-dd HH:mm}. Collect each run into a new folder, so old reports aren't merged in.");
// The oldest report's time, so a file edited between two runs merged here still warns.
var written = reports[0].LastWriteTimeUtc;

// macOS file systems ignore case by default, as Windows does.
var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
var pathComparer = StringComparer.FromComparison(pathComparison);
var sorted = StringComparer.InvariantCultureIgnoreCase;
var kinds = new Dictionary<string, ProjectKind>(pathComparer);
// Lambdas and state machines are separate classes in the report, so every class's lines are merged by source file.
var sources = new Dictionary<string, Source>(pathComparer);
foreach (var reportFile in reports)
{
    var coverage = XDocument.Load(reportFile.FullName).Root;
    // coverlet and ReportGenerator name files relative to the report's source folders.
    var folders = coverage?.Element("sources")?.Elements("source").Select(s => s.Value.Trim()).ToArray() ?? [];
    // A file has a class element for each of its classes, lambdas and state machines, so each name is looked for once.
    var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
    // A line two classes list, such as a method's and its lambda's, has the branches of both.
    var reportLines = new Dictionary<Source, Dictionary<int, (int Hits, int Covered, int Branches)>>(ReferenceEqualityComparer.Instance);
    foreach (var package in coverage?.Element("packages")?.Elements("package") ?? [])
    {
        foreach (var type in package.Element("classes")?.Elements("class") ?? [])
        {
            if ((string?)type.Attribute("filename") is not { } filename) continue;
            if (!Path.IsPathRooted(filename)) filename = CollectionsMarshal.GetValueRefOrAddDefault(resolved, filename, out _) ??= folders.Select(f => Path.GetFullPath(Path.Combine(f, filename))).FirstOrDefault(File.Exists) ?? filename;
            var source = CollectionsMarshal.GetValueRefOrAddDefault(sources, filename, out _) ??= new Source((string?)package.Attribute("name") ?? "");
            var lines = CollectionsMarshal.GetValueRefOrAddDefault(reportLines, source, out _) ??= new();
            foreach (var line in type.Element("lines")?.Elements("line") ?? [])
            {
                var (covered, branches) = Branches((string?)line.Attribute("condition-coverage"));
                ref var sum = ref CollectionsMarshal.GetValueRefOrAddDefault(lines, (int)line.Attribute("number")!, out _);
                sum = (Math.Max(sum.Hits, (int)line.Attribute("hits")!), sum.Covered + covered, sum.Branches + branches);
            }
            var className = (string?)type.Attribute("name") ?? "";
            foreach (var method in type.Element("methods")?.Elements("method") ?? [])
            {
                var member = MemberName(className, (string?)method.Attribute("name") ?? "");
                var memberLines = CollectionsMarshal.GetValueRefOrAddDefault(source.MemberLines, member, out _) ??= [];
                foreach (var line in method.Element("lines")?.Elements("line") ?? []) memberLines.Add((int)line.Attribute("number")!);
            }
        }
    }
    // Of the reports, the one that ran a line most and the one that took most of its branches win.
    foreach (var (source, lines) in reportLines)
    {
        foreach (var (number, line) in lines)
        {
            ref var kept = ref CollectionsMarshal.GetValueRefOrAddDefault(source.Lines, number, out _);
            kept = (Math.Max(kept.Hits, line.Hits), Math.Max(kept.Covered, line.Covered), Math.Max(kept.Branches, line.Branches));
        }
    }
}
// Packages that ship symbols, such as Humanizer, appear with their build machine's paths (/_/src/...).
var inReport = sources.Count;
foreach (var missing in sources.Keys.Where(p => !File.Exists(p)).ToArray()) sources.Remove(missing);
if (sources.Count == 0)
{
    Console.Error.WriteLine(inReport == 0
        ? $"error: {report} names no source files"
        : $"error: none of the {inReport} source files {report} names exist here, so it was collected in another tree or on another machine");
    return 1;
}

if (since is not null) return Since(since);
if (names.Count == 0) return Ranking();

// A member named like a common BCL member, such as Count or Add, shares its count with the BCL's uses.
var bclNames = new[] { typeof(Enumerable), typeof(object), typeof(string), typeof(List<object>), typeof(Dictionary<object, object>), typeof(IDisposable), typeof(IComparable) }
    .SelectMany(t => t.GetMembers()).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
var matches = names.Select(name => (Name: name, Paths: FindSources(name))).ToArray();
var (uses, noUses) = CountUses(matches.SelectMany(m => m.Paths).ToHashSet(pathComparer).ToArray());
var table = new StringBuilder();
// A file named twice, or by its name and a type's, is shown once.
var printed = new HashSet<string>(pathComparer);
foreach (var (name, paths) in matches)
{
    if (paths.Length == 0)
    {
        Console.WriteLine($"{name}: no file in the report ends with it and no type there has that name, so no test loaded it, or it has no code that runs");
        continue;
    }
    foreach (var match in paths)
    {
        if (!printed.Add(match)) continue;
        var source = sources[match];
        var notRun = NotRun(source);
        var partly = source.Lines.Where(l => l.Value is (Hits: > 0, var covered, var branches) && covered < branches).OrderBy(l => l.Key)
            .Select(l => $"{l.Key} {Math.Round(100.0 * l.Value.Covered / l.Value.Branches, 2)}% ({l.Value.Covered}/{l.Value.Branches})").ToArray();
        var summary = $"{source.Lines.Count - notRun.Length} of {source.Lines.Count} lines run";
        Console.WriteLine($"{Relative(match)}: {summary}{Stale(match)}");
        if (notRun.Length > 0) Console.WriteLine($"  not run: {Ranges(notRun)}");
        Console.WriteLine($"  branches partly taken: {(partly.Length > 0 ? string.Join("; ", partly) : "none")}");
        table.Append($"## {Relative(match)}\n\n{summary}.\n\n");
        PrintMembers(match, source);
    }
}
if (sweepFile is not null)
{
    if (table.Length == 0) Console.WriteLine($"No sweep table written to {sweepFile}, since no named file is in the report.");
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sweepFile))!);
        File.WriteAllText(sweepFile, $"# Sweep\n\nThe uses are those uncovered.cs --callers lists, outside each member's own file.\n\n{table}## Missing cases\n\n## Bug candidates\n");
        Console.WriteLine($"Started the sweep table in {sweepFile}: fill in each member's mutants from the list and by hand, its property or none, and what was skipped and why.");
    }
}
return 0;

// Each member's name counts as a whole word in the repo's other C# files outside test code, comments and strings included, as a
// plain word match would: a method group or a using static call counts, and so does another type's member with the same name.
void PrintMembers(string file, Source source)
{
    var shortNames = ShortNames(source);
    var rows = source.MemberLines.Where(m => m.Value.Count > 0).Select(m =>
    {
        var lines = m.Value.ToArray();
        Array.Sort(lines);
        var notRun = Array.FindAll(lines, n => source.Lines.TryGetValue(n, out var l) && l.Hits == 0);
        var name = CountedName(m.Key);
        int? count = uses is not null && name is not null && uses.Total.TryGetValue(name, out var total) ? total - uses.Own[file].GetValueOrDefault(name) : null;
        var run = notRun.Length == lines.Length ? "never run" : notRun.Length > 0 ? $"not run: {Ranges(notRun)}" : "";
        var note = name is null ? "no name to count, so find its uses by reading"
            : m.Key.EndsWith(".ctor", StringComparison.Ordinal) ? "counts every use of its type's name"
            : bclNames.Contains(name) ? "a BCL name too, so the count includes its uses" : "";
        var notRunCell = notRun.Length == lines.Length ? "all" : notRun.Length > 0 ? Ranges(notRun) : "none";
        return (Member: shortNames[m.Key], Name: name, First: lines[0], Uses: count, Lines: $"{lines[0]}-{lines[^1]}", NotRun: notRunCell, Notes: string.Join("; ", new[] { run, note }.Where(s => s.Length > 0)));
    }).OrderBy(r => r.First).ThenBy(r => r.Member, sorted).ToArray();
    if (rows.Length == 0) return;
    var width = rows.Max(r => r.Member.Length);
    var lineWidth = rows.Max(r => r.Lines.Length);
    Console.WriteLine(uses is null ? $"  members in source order (uses not counted: {noUses}):"
        : $"  members in source order, with the uses of each name elsewhere in the repo outside tests{(uses.Sites is null ? "" : ", and under each up to 4 of its uses outside tests and in them, one per file")}:");
    table.Append("| Member | Lines | Not run, and why | Uses outside tests | Uses in tests | Listed | By hand | Property | Skipped, and why |\n|---|---|---|---|---|---|---|---|---|\n");
    foreach (var row in rows)
    {
        Console.WriteLine($"    {row.Member.PadRight(width)} {row.Uses,5}  lines {row.Lines.PadRight(lineWidth)}  {row.Notes}".TrimEnd());
        var (outside, inTests) = uses is null ? ("not counted", "not counted")
            : row.Name is null ? ("by reading", "by reading")
            : uses.Sites is { } sites ? PrintCallers(file, row.Member, row.Name, sites[row.Name])
            : ("", "");
        table.Append($"| `{row.Member}` | {row.Lines} | {row.NotRun} | {outside} | {inTests} | | | | |\n");
    }
    table.Append('\n');
}

// Text can't tell which member a name means, so a use is a name after a dot, or a bare name assigned to, as in an object initializer, and a
// constructor's is new and its type's name: other bare names are mostly declarations and types. When some uses are written after the type,
// as in PCG.Parse, only those are listed, since the rest are mostly another type's member of the same name. Otherwise uses through a
// variable named like the type, such as pcg for PCG, come first.
(string Outside, string InTests) PrintCallers(string file, string member, string name, List<Site> found)
{
    var dot = member.LastIndexOf('.');
    var type = Innermost(member[..dot]);
    var ctor = member[(dot + 1)..] == "ctor";
    var ownEntry = uses!.Entries.GetValueOrDefault(file);
    var listed = found.Where(s => s.File != ownEntry && (ctor ? s.AfterNew : s.Qualifier is not null || s.Assigned)).ToArray();
    var typed = !ctor && Array.Exists(listed, s => s.Qualifier == type);
    if (typed) listed = Array.FindAll(listed, s => s.Qualifier == type);
    else if (!ctor && bclNames.Contains(name))
    {
        Console.WriteLine($"      none written {type}.{name}, so find its uses with find-references");
        return ("find-references", "find-references");
    }
    var how = ctor ? $" as new {type}" : typed ? $" as {type}.{name}" : "";
    foreach (var inTests in new[] { false, true })
    {
        var side = Array.FindAll(listed, s => s.InTests == inTests);
        var label = $"      {(inTests ? "in tests" : "outside tests")}{how}";
        if (side.Length == 0)
        {
            Console.WriteLine($"{label}: none");
            continue;
        }
        var shown = side.OrderBy(s => s.Qualifier is { Length: >= 3 } q && (type.EndsWith(q, StringComparison.OrdinalIgnoreCase) || type.StartsWith(q, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .DistinctBy(s => s.File).Take(4).OrderBy(s => s.File, StringComparer.Ordinal).ToArray();
        var fileCount = side.Select(s => s.File).ToHashSet().Count;
        Console.WriteLine($"{label}: {Plural(side.Length, "use")} in {Plural(fileCount, "file")}{(shown.Length < side.Length ? $", {shown.Length} shown" : "")}:");
        foreach (var site in shown)
        {
            var full = Path.GetFullPath(Path.Combine(uses.Root, site.File));
            Console.WriteLine($"        {Relative(full)}:{site.Line}  {CallText(full, site.Line, site.Column)}");
        }
    }
    return ($"{listed.Count(s => !s.InTests)}", $"{listed.Count(s => s.InTests)}");
}

(Uses? Uses, string Why) CountUses(string[] files)
{
    if (files.Length == 0) return (null, "");
    if (GitTop(Path.GetDirectoryName(files[0])!, out var why) is not { } repoRoot) return (null, why);
    if (Git(repoRoot, out why, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.cs", "*.csproj") is not { } listed) return (null, why);
    var total = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var file in files)
    {
        foreach (var member in sources[file].MemberLines.Keys)
        {
            if (CountedName(member) is { } name) total[name] = 0;
        }
    }
    // One git call lists the source files and the projects, which saves a second walk of the untracked files.
    var paths = listed.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    var listedFiles = paths.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToHashSet();
    var projects = ProjectDirectories(repoRoot, Projects(repoRoot, paths.Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))));
    // A named file finds its own entry by the longest repo path it ends with, since the report can reach the repo through a junction or symlink.
    var own = new Dictionary<string, Dictionary<string, int>>(pathComparer);
    var entries = new Dictionary<string, string>(pathComparer);
    var ownByEntry = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
    foreach (var file in files)
    {
        own.Add(file, new Dictionary<string, int>(StringComparer.Ordinal));
        var portable = file.Replace('\\', '/');
        if (listedFiles.Where(r => EndsWithSegments(portable, r, '/', pathComparison)).MaxBy(r => r.Length) is not { } entry) continue;
        ownByEntry[entry] = own[file];
        entries.Add(file, entry);
    }
    var sites = total.Keys.ToDictionary(name => name, _ => new List<Site>(), StringComparer.Ordinal);
    var lookup = total.GetAlternateLookup<ReadOnlySpan<char>>();
    var siteLookup = sites.GetAlternateLookup<ReadOnlySpan<char>>();
    var names = SearchValues.Create(total.Keys.ToArray(), StringComparison.Ordinal);
    var newerTests = 0;
    foreach (var relative in listedFiles)
    {
        // Uses in tests never count, and are only listed for --callers.
        var inTests = ProjectOf(projects, relative) is { } project && Kind(project) != ProjectKind.Code;
        if (inTests && !callers) continue;
        var file = Path.GetFullPath(Path.Combine(repoRoot, relative));
        if (!File.Exists(file)) continue;
        var here = inTests ? null : ownByEntry.GetValueOrDefault(relative);
        var generated = relative.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
        var text = ReadText(file);
        int line = 1, counted = 0;
        var mentions = false;
        // Most words aren't a counted name, so the search jumps from one name to the next and takes the whole word there.
        for (int next = 0, found; (found = text.AsSpan(next).IndexOfAny(names)) >= 0;)
        {
            var index = next + found;
            var length = WordLength(text.AsSpan(index));
            next = index + length;
            if (index > 0 && IsWordChar(text[index - 1])) continue;
            var token = text.AsSpan(index, length);
            ref var count = ref CollectionsMarshal.GetValueRefOrNullRef(lookup, token);
            if (Unsafe.IsNullRef(ref count)) continue;
            mentions = true;
            if (!inTests) count++;
            if (here is not null) ++CollectionsMarshal.GetValueRefOrAddDefault(here, token.ToString(), out _);
            if (!callers || generated) continue;
            line += text.AsSpan(counted, index - counted).Count('\n');
            counted = index;
            var lineStart = text.LastIndexOf('\n', index) + 1;
            var lead = text.AsSpan(lineStart, index - lineStart);
            if (Unlisted(lead)) continue;
            siteLookup[token].Add(new Site(relative, line, index - lineStart, Qualifier(lead), AfterNew(lead), Assigned(lead, text.AsSpan(index + length)), inTests));
        }
        // A test of these members written after the report would run lines it shows as not run.
        if (inTests && mentions && File.GetLastWriteTimeUtc(file) > written) newerTests++;
    }
    if (newerTests > 0) Console.WriteLine($"warning: {Plural(newerTests, "test file")} changed after the report was written, so collect coverage again before trusting the lines not run.");
    return (new Uses(total, own, entries, callers ? sites : null, repoRoot), "");
}

// Null for a bare name. After a dot, the word before it, as PCG in PCG.Parse, or "" when there's none or it's in a nameof.
static string? Qualifier(ReadOnlySpan<char> lead)
{
    if (!lead.EndsWith('.')) return null;
    var start = lead.Length - 1;
    while (start > 0 && IsWordChar(lead[start - 1])) start--;
    return start == lead.Length - 1 || lead[..start].EndsWith("nameof(", StringComparison.Ordinal) ? "" : lead[start..^1].ToString();
}

// A bare name assigned to at the start of a line or after { or a comma, as in an object initializer, rather than a field declared with a value.
static bool Assigned(ReadOnlySpan<char> lead, ReadOnlySpan<char> rest)
{
    var before = lead.TrimEnd();
    var after = rest.TrimStart();
    return (before.IsEmpty || before[^1] is '{' or ',') && after.Length > 1 && after[0] == '=' && after[1] is not ('=' or '>');
}

// Not a use: a name in a comment, a using directive or a namespace declaration, or in a string, which an odd number of quotes before it shows.
static bool Unlisted(ReadOnlySpan<char> lead)
{
    var start = lead.TrimStart();
    if (start.StartsWith('*') || lead.Contains("//", StringComparison.Ordinal) || lead.Contains("/*", StringComparison.Ordinal) || start.StartsWith("namespace ", StringComparison.Ordinal)) return true;
    if ((start.StartsWith("using ", StringComparison.Ordinal) || start.StartsWith("global using ", StringComparison.Ordinal)) && start.IndexOfAny('(', '=') < 0 && !start.Contains("var ", StringComparison.Ordinal)) return true;
    var quotes = 0;
    for (var k = 0; k < lead.Length; k++)
    {
        if (lead[k] == '"' && (k == 0 || lead[k - 1] is not ('\\' or '\''))) quotes++;
    }
    return (quotes & 1) == 1;
}

static bool AfterNew(ReadOnlySpan<char> lead)
{
    var trimmed = lead.TrimEnd();
    return trimmed.Length < lead.Length && trimmed.EndsWith("new", StringComparison.Ordinal) && (trimmed.Length == 3 || !IsWordChar(trimmed[^4]));
}

static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

static int WordLength(ReadOnlySpan<char> text)
{
    var length = 0;
    while (length < text.Length && IsWordChar(text[length])) length++;
    return length;
}

// The line a use is on, run on to the lines its brackets continue to, as one line.
static string CallText(string path, int line, int column)
{
    var lines = CollectionsMarshal.GetValueRefOrAddDefault(_fileLines, path, out _) ??= ReadText(path).Split('\n');
    if (line > lines.Length) return "";
    var text = new StringBuilder(lines[line - 1].Trim());
    var depth = Depth(lines[line - 1].AsSpan(Math.Min(column, lines[line - 1].Length)));
    for (var next = line; depth > 0 && next < lines.Length && next < line + 8 && text.Length < 160; next++)
    {
        text.Append(' ').Append(lines[next].Trim());
        depth += Depth(lines[next]);
    }
    var call = Whitespace.Replace(text.ToString(), " ").Replace("( ", "(").Replace("[ ", "[").Replace(" )", ")").Replace(" ]", "]");
    return call.Length > 160 ? call[..157] + "..." : call;
}

static int Depth(ReadOnlySpan<char> text) => text.Count('(') - text.Count(')');

// ReadAllText decodes in small chunks, several times slower on a big repo's files than one GetString. A UTF-16 or UTF-32 file, told
// by its BOM, is left to ReadAllText, so the text is the same.
static string ReadText(string path)
{
    var bytes = File.ReadAllBytes(path);
    return bytes is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0, 0, 0xFE, 0xFF, ..] ? File.ReadAllText(path) : Encoding.UTF8.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0));
}

int Since(string reference)
{
    // git runs at the top of the repo the report's sources are in, so a pathspec isn't limited to the current folder.
    if (RepoRoot(out var why) is not { } repoRoot)
    {
        Console.Error.WriteLine($"error: --since needs a git repo: {why}");
        return 1;
    }
    var mergeBase = Git(repoRoot, out why, "merge-base", reference, "HEAD")?.Trim();
    if (string.IsNullOrEmpty(mergeBase))
    {
        Console.Error.WriteLine($"error: git couldn't find the merge base of {reference} and HEAD in {repoRoot}{(why.Length > 0 ? $": {why}" : "")}");
        return 1;
    }
    // A new untracked file counts as changed on every line, shown here as null.
    var changed = new Dictionary<string, HashSet<int>?>(pathComparer);
    HashSet<int>? current = null;
    // The prefixes are set because diff.noprefix and diff.mnemonicPrefix change them, and git adds a tab after a path with a space.
    foreach (var line in (Git(repoRoot, out _, "-c", "core.quotePath=false", "diff", "-U0", "--no-color", "--no-ext-diff", "--src-prefix=a/", "--dst-prefix=b/", mergeBase, "--", "*.cs") ?? "").Split('\n'))
    {
        var header = DiffTarget.Match(line);
        if (header.Success)
        {
            current = header.Groups[1] is { Success: true } target ? CollectionsMarshal.GetValueRefOrAddDefault(changed, target.Value, out _) ??= [] : null;
            continue;
        }
        var hunk = Hunk.Match(line);
        if (current is null || !hunk.Success) continue;
        var start = int.Parse(hunk.Groups[1].ValueSpan);
        var count = hunk.Groups[2] is { Success: true } length ? int.Parse(length.ValueSpan) : 1;
        // A deletion adds no line, so the lines either side of it count as changed.
        if (count == 0) current.UnionWith([start, start + 1]);
        for (var number = start; number < start + count; number++) current.Add(number);
    }
    foreach (var added in (Git(repoRoot, out _, "ls-files", "-z", "--others", "--exclude-standard", "--full-name", "--", "*.cs") ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries)) changed[added] = null;

    var projects = ProjectDirectories(repoRoot, RepoProjects(repoRoot));
    // The report can reach the repo by another path, such as through a junction, so a file it doesn't have at its repo path is looked for by name.
    var byFileName = sources.Keys.ToLookup(p => Path.GetFileName(p), pathComparer);
    var named = names.Select(Named).ToArray();
    var unmatched = named.ToHashSet();
    var missing = new List<(string Relative, string? Project)>();
    var testCode = 0;
    int totalRun = 0, total = 0;
    foreach (var (relative, lines) in changed.OrderBy(c => c.Key, sorted))
    {
        var full = Path.GetFullPath(Path.Combine(repoRoot, relative));
        var paths = sources.ContainsKey(full) ? [full] : byFileName[Path.GetFileName(relative)].Where(p => EndsWithPath(p, relative)).Order(sorted).ToArray();
        // A path to a file in the report names it by its path there, which can differ from its path in the repo.
        var naming = Array.FindAll(named, n => Names(n, full) || Array.Exists(paths, p => Names(n, p)));
        if (named.Length > 0 && naming.Length == 0) continue;
        unmatched.ExceptWith(naming);
        // Test code is told by its project, so a production file named like a test, such as PrimalityTest.cs, still counts.
        var project = ProjectOf(projects, relative);
        if (project is not null && Kind(project) != ProjectKind.Code)
        {
            testCode++;
            continue;
        }
        if (paths.Length == 0)
        {
            missing.Add((relative, project));
            continue;
        }
        foreach (var match in paths)
        {
            var source = sources[match];
            var code = source.Lines.Keys.Where(n => lines is null || lines.Contains(n)).ToArray();
            Array.Sort(code);
            var nonCode = lines is null ? [] : NotCode(match, lines.Where(n => !source.Lines.ContainsKey(n)));
            if (code.Length == 0 && nonCode.Length == 0) continue;
            var notRun = Array.FindAll(code, n => source.Lines[n].Hits == 0);
            var members = ChangedMembers(source, lines);
            totalRun += code.Length - notRun.Length;
            total += code.Length;
            Console.WriteLine($"{Relative(match)}: {code.Length - notRun.Length} of {code.Length} changed lines of code run{Stale(match)}");
            if (notRun.Length > 0) Console.WriteLine($"  changed lines not run: {Ranges(notRun)}");
            if (nonCode.Length > 0) Console.WriteLine($"  changed lines that aren't code, such as a declaration or the lines around a deletion: {Ranges(nonCode)}");
            if (members.Length > 0) Console.WriteLine($"  changed members: {string.Join(", ", members)}");
        }
    }
    foreach (var name in named.Where(unmatched.Contains)) Console.WriteLine($"{name.Text}: no changed file ends with it, and --since takes file names and partial paths, not type names");
    var assemblies = sources.Values.Select(s => s.Assembly).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var byProject = missing.GroupBy(m => m.Project is { } project ? Path.GetFileNameWithoutExtension(project) : "(no project)", StringComparer.OrdinalIgnoreCase).ToArray();
    var loaded = byProject.Where(g => assemblies.Contains(g.Key)).SelectMany(g => g).Select(m => m.Relative).ToArray();
    var other = byProject.Where(g => !assemblies.Contains(g.Key)).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ({g.Count()})").ToArray();
    if (loaded.Length > 0) Console.WriteLine($"Changed but not in this report, so no executable code or no test loaded them: {string.Join(", ", loaded)}");
    if (other.Length > 0) Console.WriteLine($"Changed in projects these tests don't load (run their own tests' coverage): {string.Join(", ", other)}");
    if (testCode > 0) Console.WriteLine($"Changed in test and benchmark projects, so not counted: {Plural(testCode, "file")}.");
    Console.WriteLine($"Since {reference} (merge base {mergeBase[..10]}): {totalRun} of {total} changed lines of code run.");
    return 0;
}

int Ranking()
{
    var repoRoot = RepoRoot(out _);
    var repoTestProjects = repoRoot is null ? [] : Array.FindAll(RepoProjects(repoRoot), p => Kind(p) == ProjectKind.Test);
    // Other projects the tests load have their own tests, so their lines not run here say little. VSTest's report has the test
    // project, whose references are the projects under test. Microsoft.Testing.Platform's leaves the test project out, and so do
    // references written with MSBuild properties, and then the projects under test are the reported ones no other reported project references.
    var reported = sources.GroupBy(s => s.Value.Assembly, StringComparer.OrdinalIgnoreCase)
        .Select(g => (Assembly: g.Key, Project: AssemblyProject(g.Key, g.Select(s => Path.GetDirectoryName(s.Key)!).Distinct(pathComparer))))
        .Where(p => p.Project is not null)
        .ToArray();
    var kindOf = reported.ToDictionary(p => p.Assembly, p => Kind(p.Project!), StringComparer.OrdinalIgnoreCase);
    var testProjects = reported.Where(p => kindOf[p.Assembly] == ProjectKind.Test).Select(p => p.Project!).ToArray();
    var libraries = reported.Where(p => kindOf[p.Assembly] == ProjectKind.Code).Select(p => p.Project!).ToArray();
    var referenced = libraries.SelectMany(ProjectReferences).ToHashSet(pathComparer);
    var fromTests = testProjects.SelectMany(ProjectReferences).Where(p => Array.Exists(libraries, l => pathComparer.Equals(l, p))).ToArray();
    var underTest = (fromTests.Length > 0 ? fromTests : libraries.Where(p => !referenced.Contains(p))).ToHashSet(pathComparer);
    var underTestAssemblies = reported.Where(p => underTest.Contains(p.Project!)).Select(p => p.Assembly).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var rows = new List<(int NotRun, int Lines, string Path, string Assembly, bool Ranked)>();
    foreach (var (sourcePath, source) in sources)
    {
        // An assembly whose project isn't found goes by its name.
        var code = (kindOf.TryGetValue(source.Assembly, out var kind) ? kind : KindByName(source.Assembly)) == ProjectKind.Code;
        if (!code || sourcePath.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase)) continue;
        var notRun = NotRun(source).Length;
        if (notRun == 0) continue;
        var ranked = all || underTestAssemblies.Count == 0 || underTestAssemblies.Contains(source.Assembly);
        rows.Add((notRun, source.Lines.Count, Relative(sourcePath), source.Assembly, ranked));
    }
    var rankedRows = rows.Where(r => r.Ranked).OrderByDescending(r => r.NotRun).ThenBy(r => r.Path, sorted).ToArray();
    foreach (var row in rankedRows.Take(top)) Console.WriteLine($"{row.NotRun,5} of {row.Lines,5} lines not run: {row.Path}");
    Console.WriteLine(rankedRows.Length > 0 ? $"Lines not run by project: {ByAssembly(rankedRows)}. Name files to see their lines and members." : "Every line ran in the projects under test.");
    var others = rows.Where(r => !r.Ranked).ToArray();
    if (others.Length > 0) Console.WriteLine($"Not ranked, since they aren't the projects under test (--all ranks them too): {ByAssembly(others)}");
    var stale = sources.Keys.Count(p => File.GetLastWriteTimeUtc(p) > written);
    if (stale > 0) Console.WriteLine($"warning: {Plural(stale, "file")} changed after the report was written, so collect coverage again before trusting their lines.");
    if (repoRoot is not null)
    {
        // A project has tests of its own when a test project is named after it, such as MyLib.Tests, MyLibTests or MyLib.Specs,
        // or references nothing else, such as a lone Tests project beside the one library it tests.
        var testedAlone = repoTestProjects
            .Select(p => ProjectReferences(p).Where(r => Kind(r) == ProjectKind.Code).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase))
            .Where(references => references.Count == 1)
            .Select(references => references.First())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var testProjectNames = Array.ConvertAll(repoTestProjects, p => Path.GetFileNameWithoutExtension(p));
        var noTests = rows.Select(r => r.Assembly).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .Where(a => !testedAlone.Contains(a) && !Array.Exists(testProjectNames, n => n.StartsWith(a, StringComparison.OrdinalIgnoreCase) && TestSuffix.IsMatch(n.AsSpan(a.Length))))
            .ToArray();
        Array.Sort(noTests, sorted);
        if (noTests.Length > 0) Console.WriteLine($"With no test project of their own, so the best place for new tests: {string.Join(", ", noTests)}");
    }
    return 0;
}

int Usage()
{
    Console.Error.WriteLine("usage: dotnet run --file uncovered.cs -- <results folder or .cobertura.xml> [file or type names, or partial paths] [--callers] [--sweep <file>] [--since <ref>] [--top 20] [--all]");
    return 2;
}

// A path to a file, relative or absolute, names that file. Any other name, or a path the report doesn't have, since the report can reach
// a file by another path, such as through a junction, names the paths that end with it in whole segments; a name with no extension, such
// as a type's, is tried as a .cs file too.
bool Names((string Text, string Full, bool InReport) name, string path) =>
    pathComparer.Equals(name.Full, path) || !name.InReport && (EndsWithPath(path, name.Text) || !Path.HasExtension(name.Text) && EndsWithPath(path, name.Text + ".cs"));

(string Text, string Full, bool InReport) Named(string text)
{
    var full = Path.GetFullPath(text);
    return (text, full, sources.ContainsKey(full));
}

// A type's file can have another name, so a name no file has is looked for among the report's types.
string[] FindSources(string name)
{
    var named = Named(name);
    var found = sources.Keys.Where(p => Names(named, p)).ToArray();
    if (found.Length == 0) found = sources.Where(s => s.Value.MemberLines.Keys.Any(m => IsTypeNamed(m.AsSpan(0, m.LastIndexOf('.')), name))).Select(s => s.Key).ToArray();
    Array.Sort(found, sorted);
    return found;
}

// The current folder's repo when it holds any of the report's sources, else the first source's.
string? RepoRoot(out string why)
{
    var here = GitTop(Environment.CurrentDirectory, out why);
    if (here is not null && sources.Keys.Any(s => IsInFolder(s, here, pathComparison))) return here;
    var first = GitTop(Path.GetDirectoryName(sources.Keys.First())!, out var firstWhy);
    if (first is null && here is null) why = firstWhy;
    return first ?? here;
}

string Stale(string file) => File.GetLastWriteTimeUtc(file) > written ? " (changed after the report was written, so collect coverage again before trusting these lines)" : "";

// The changed lines that coverage doesn't count as code, leaving out blank lines, braces and comments.
static int[] NotCode(string file, IEnumerable<int> numbers)
{
    var text = File.ReadAllLines(file);
    var notCode = numbers.Where(n => n >= 1 && n <= text.Length && text[n - 1].Trim() is { Length: > 0 } line && line is not ("{" or "}" or "};") && !line.StartsWith("//", StringComparison.Ordinal)).ToArray();
    Array.Sort(notCode);
    return notCode;
}

// A changed line belongs to the members on the first line of code at or after it, which takes in their signatures and attributes,
// but the lines before a file's first line of code belong to none.
static string[] ChangedMembers(Source source, HashSet<int>? lines)
{
    var shortNames = ShortNames(source);
    var owners = source.MemberLines.SelectMany(m => m.Value, (m, line) => (Line: line, Member: shortNames[m.Key])).ToLookup(p => p.Line, p => p.Member);
    var code = owners.Select(g => g.Key).ToArray();
    Array.Sort(code);
    var members = new HashSet<string>(StringComparer.Ordinal);
    foreach (var line in lines ?? (IEnumerable<int>)code)
    {
        var at = Array.BinarySearch(code, line);
        if (at < 0) at = ~at;
        if (at < code.Length && (at > 0 || code[0] == line)) members.UnionWith(owners[code[at]]);
    }
    var changed = members.ToArray();
    Array.Sort(changed, StringComparer.InvariantCultureIgnoreCase);
    return changed;
}

// A type shows as the fewest of its trailing names that no other type in its file ends with, so GenInt.Range and GenLong.Range stay apart.
static Dictionary<string, string> ShortNames(Source source)
{
    var members = source.MemberLines.Where(m => m.Value.Count > 0).Select(m => (Member: m.Key, Type: m.Key[..m.Key.LastIndexOf('.')])).ToArray();
    var types = members.Select(m => m.Type).ToHashSet(StringComparer.Ordinal);
    var shortTypes = types.ToDictionary(type => type, type => ShortType(type, types), StringComparer.Ordinal);
    return members.ToDictionary(m => m.Member, m => shortTypes[m.Type] + m.Member[m.Type.Length..], StringComparer.Ordinal);
}

static string ShortType(string type, HashSet<string> types)
{
    for (var dot = type.LastIndexOf('.'); dot > 0; dot = type.LastIndexOf('.', dot - 1))
    {
        var tail = type[(dot + 1)..];
        if (!types.Any(other => other != type && IsTypeNamed(other, tail))) return tail;
    }
    return type;
}

// A type's full name names it, and so do its whole trailing names, as GenInt.Range does CsCheck.GenInt.Range.
static bool IsTypeNamed(ReadOnlySpan<char> type, ReadOnlySpan<char> name) => type.Equals(name, StringComparison.OrdinalIgnoreCase) || EndsWithSegments(type, name, '.', StringComparison.OrdinalIgnoreCase);

static bool EndsWithPath(string path, string name)
{
    var tail = name.Replace('/', '\\').AsSpan();
    if (tail.StartsWith(@".\")) tail = tail[2..];
    return EndsWithSegments(path.Replace('/', '\\'), tail.TrimStart('\\'), '\\', StringComparison.OrdinalIgnoreCase);
}

static bool EndsWithSegments(ReadOnlySpan<char> path, ReadOnlySpan<char> tail, char separator, StringComparison comparison) =>
    path.Length > tail.Length && path[^(tail.Length + 1)] == separator && path.EndsWith(tail, comparison);

static bool IsInFolder(string path, string folder, StringComparison comparison) =>
    path.Length > folder.Length && path[folder.Length] == Path.DirectorySeparatorChar && path.StartsWith(folder, comparison);

static string? GitTop(string directory, out string why) => Git(directory, out why, "rev-parse", "--show-toplevel")?.Trim() is { Length: > 0 } top ? Path.GetFullPath(top) : null;

static int[] NotRun(Source source)
{
    var notRun = source.Lines.Where(l => l.Value.Hits == 0).Select(l => l.Key).ToArray();
    Array.Sort(notRun);
    return notRun;
}

// condition-coverage reads as 50% (1/2), and the counts are what add up.
static (int Covered, int Branches) Branches(string? condition)
{
    var counts = condition.AsSpan(condition.AsSpan().LastIndexOf('(') + 1).TrimEnd(')');
    var slash = counts.IndexOf('/');
    return slash > 0 && int.TryParse(counts[..slash], out var covered) && int.TryParse(counts[(slash + 1)..], out var branches) ? (covered, branches) : (0, 0);
}

static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

static string Relative(string path) => Path.GetRelativePath(Environment.CurrentDirectory, path);

static string ByAssembly(IEnumerable<(int NotRun, int Lines, string Path, string Assembly, bool Ranked)> rows) =>
    string.Join(", ", rows.GroupBy(r => r.Assembly, StringComparer.OrdinalIgnoreCase)
        .Select(g => (Name: g.Key, Files: g.Count(), NotRun: g.Sum(r => r.NotRun)))
        .OrderByDescending(t => t.NotRun)
        .Select(t => $"{t.Name} {t.NotRun} in {Plural(t.Files, "file")}"));

static string Ranges(int[] numbers)
{
    var ranges = new List<string>();
    for (var i = 0; i < numbers.Length; i++)
    {
        var start = numbers[i];
        while (i + 1 < numbers.Length && numbers[i + 1] == numbers[i] + 1) i++;
        ranges.Add(start == numbers[i] ? $"{start}" : $"{start}-{numbers[i]}");
    }
    return string.Join(", ", ranges);
}

// The member a person would name for a cobertura method, after its type's full name: lambdas, local functions and state machines fold
// into the member that contains them.
static string MemberName(string className, string methodName)
{
    // A file-local type, as source generators write, is named after its file too, as in <Names>F8865D37...__Hidden, which would read as a state machine.
    if (className.Contains(">F", StringComparison.Ordinal)) className = FileLocalPrefix.Replace(className, "");
    // These patterns need a '<', or coverlet's '`' for a generic type, which most names lack.
    var type = className.AsSpan().IndexOfAny('<', '`') >= 0 ? TypeArguments.Replace(className, "") : className;
    // A lambda in an explicit implementation is named after the interface, as in <System.Collections.Generic.IEnumerable<T>.GetEnumerator>b__3_0.
    if (methodName.Contains('<')) methodName = TypeArguments.Replace(methodName, "");
    var containing = methodName.StartsWith('<') && OwnerName.Match(methodName) is { Success: true, Index: 0 } local ? local.Groups[1].Value : methodName;
    if (type.Contains('<') && NestedGenerated.Match(type) is { Success: true } generated)
    {
        type = generated.Groups[1].Value;
        // A state machine's name writes an interface's dots as dashes, as in <System-Collections-IEnumerable-GetEnumerator>d__4.
        if (OwnerName.Match(generated.Groups[2].Value) is { Success: true } outer) containing = outer.Groups[1].Value.Replace('-', '.');
    }
    // An explicit interface member is named after its interface, as in IEnumerator<T>.get_Current.
    if (!containing.StartsWith('.') && containing.LastIndexOf('.') is > 0 and var dot) containing = containing[(dot + 1)..];
    var member = containing switch
    {
        _ when Constructor.IsMatch(containing) => "ctor",
        _ when Indexer.IsMatch(containing) => "this",
        _ when Accessor.Match(containing) is { Success: true } accessor => accessor.Groups[2].Value,
        _ => containing,
    };
    // coverlet writes a nested type after a slash.
    return $"{type.Replace('/', '.').Replace('+', '.')}.{member}";
}

// The word a use of the member is written with: a constructor's type name, and nothing for an indexer or an operator.
static string? CountedName(string member)
{
    var dot = member.LastIndexOf('.');
    var name = member.AsSpan(dot + 1) is "ctor" ? Innermost(member[..dot]) : member[(dot + 1)..];
    var length = WordLength(name);
    return name == "this" || name.StartsWith("op_", StringComparison.Ordinal) || length == 0 ? null : name[..length];
}

static string Innermost(string type) => type[(type.LastIndexOf('.') + 1)..];

// A project is told as dotnet test tells it, by IsTestProject and IsTestingPlatformApplication, which the props of
// Microsoft.NET.Test.Sdk, Microsoft.Testing.Platform.MSBuild and TUnit.Engine set and a project file can set false.
// Those props come with restore, so they're read from its project.assets.json, and a project not restored goes by its name.
ProjectKind Kind(string project)
{
    if (kinds.TryGetValue(project, out var known)) return known;
    var name = Path.GetFileNameWithoutExtension(project);
    var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
    ProjectKind kind;
    if (!File.Exists(assets)) kind = KindByName(name);
    else
    {
        var restored = ReadText(assets);
        bool Has(SearchValues<string> packages) => restored.AsSpan().ContainsAny(packages);
        var document = XDocument.Load(project);
        var platform = Has(_testPlatforms);
        // A helper, such as MyLib.TestUtilities or a benchmark project, has test packages it opts out of, a test framework or
        // BenchmarkDotNet but no test platform, or a helper's name.
        kind = (Declared(document, "IsTestProject") ?? platform) || Declared(document, "IsTestingPlatformApplication") == true ? ProjectKind.Test
            : platform || Has(_helpers) || HelperName.IsMatch(name) ? ProjectKind.Helper
            : ProjectKind.Code;
    }
    return kinds[project] = kind;
}

static ProjectKind KindByName(string name) => TestProjectName.IsMatch(name) ? ProjectKind.Test : HelperName.IsMatch(name) ? ProjectKind.Helper : ProjectKind.Code;

// The value a project file sets with no condition, which wins over a package's props, since the project is read after them.
static bool? Declared(XDocument project, string property) =>
    project.Descendants().LastOrDefault(e => e.Name.LocalName == property && e.Attribute("Condition") is null && e.Parent?.Attribute("Condition") is null) is { } element
    && bool.TryParse(element.Value.Trim(), out var value)
        ? value
        : null;

string[] RepoProjects(string repoRoot) =>
    Projects(repoRoot, (Git(repoRoot, out _, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.csproj") ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries));

static string[] Projects(string repoRoot, IEnumerable<string> listed) => listed.ToHashSet().Select(p => Path.GetFullPath(Path.Combine(repoRoot, p))).Where(File.Exists).ToArray();

// Each repo project's directory, relative and with forward slashes, and its project; the first project in a directory wins, as in FindProject.
Dictionary<string, string> ProjectDirectories(string repoRoot, string[] projects)
{
    Array.Sort(projects, StringComparer.OrdinalIgnoreCase);
    var directories = new Dictionary<string, string>(pathComparer);
    foreach (var project in projects)
    {
        var directory = Path.GetRelativePath(repoRoot, Path.GetDirectoryName(project)!).Replace('\\', '/');
        directories.TryAdd(directory == "." ? "" : directory, project);
    }
    return directories;
}

// A file belongs to the nearest project above it.
static string? ProjectOf(Dictionary<string, string> directories, string relative)
{
    var lookup = directories.GetAlternateLookup<ReadOnlySpan<char>>();
    for (var slash = relative.LastIndexOf('/'); slash > 0; slash = relative.LastIndexOf('/', slash - 1))
    {
        if (lookup.TryGetValue(relative.AsSpan(0, slash), out var project)) return project;
    }
    return directories.GetValueOrDefault("");
}

// A reference written with an MSBuild property can't be read without MSBuild, so it's left out.
static IEnumerable<string> ProjectReferences(string project) =>
    XDocument.Load(project).Descendants().Where(e => e.Name.LocalName == "ProjectReference")
        .Select(r => (string?)r.Attribute("Include"))
        .OfType<string>()
        .Where(include => !include.Contains("$(", StringComparison.Ordinal))
        .Select(include => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', Path.DirectorySeparatorChar))));

// A linked file is under another project, or none, so the project named after the assembly wins over the first one found.
static string? AssemblyProject(string assembly, IEnumerable<string> directories)
{
    string? first = null;
    foreach (var directory in directories)
    {
        if (FindProject(directory) is not { } project) continue;
        if (Path.GetFileNameWithoutExtension(project).Equals(assembly, StringComparison.OrdinalIgnoreCase)) return project;
        first ??= project;
    }
    return first;
}

static string? FindProject(string? directory)
{
    for (; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
    {
        if (Directory.Exists(directory) && Directory.GetFiles(directory, "*.csproj").Min(StringComparer.OrdinalIgnoreCase) is { } project) return project;
    }
    return null;
}

// why is git's first line of error, or that git isn't there.
static string? Git(string directory, out string why, params string[] arguments)
{
    // git writes paths in UTF-8 whatever the console's code page.
    var startInfo = new ProcessStartInfo("git", arguments) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
    try
    {
        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        why = errors.Result.Split('\n', 2)[0].Trim();
        return process.ExitCode == 0 ? output : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        why = "git isn't on the PATH";
        return null;
    }
}

partial class Program
{
    [GeneratedRegex(@"\s+")]
    static partial Regex Whitespace { get; }

    [GeneratedRegex(@"^\+\+\+ (?:b/(.+?)|/dev/null)\t?\r?$")]
    static partial Regex DiffTarget { get; }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")]
    static partial Regex Hunk { get; }

    // Before a restore: Tests, MyLib.Tests, MyLibTests, MyLib.UnitTests and MyLib.Tests.Unit are test projects; Latest, Backtest and test-data aren't.
    [GeneratedRegex(@"(?:^|[._\- ])(?i:tests?)$|(?:^|[._\- ])(?i:tests)[._\- ]|(?<=[a-z0-9])Tests?$")]
    static partial Regex TestProjectName { get; }

    // Test helpers such as MyLib.TestUtilities, MyLib.TestHelpers and MyLib.Testing, and benchmarks such as MyLib.Benchmarks; BackTesting isn't one.
    [GeneratedRegex(@"(?:^|[._\- ])(?i:testutilities|testhelpers|testing|benchmarks?)$|(?<=[a-z0-9])(?:TestUtilities|TestHelpers|Benchmarks?)$")]
    static partial Regex HelperName { get; }

    static readonly SearchValues<string> _testPlatforms = Packages("Microsoft.NET.Test.Sdk", "Microsoft.Testing.Platform.MSBuild", "TUnit.Engine");
    static readonly SearchValues<string> _helpers = Packages("NUnit", "xunit.core", "xunit.v3.extensibility.core", "MSTest.TestFramework", "TUnit.Core", "BenchmarkDotNet");

    // A file with several sites shown is read once.
    static readonly Dictionary<string, string[]> _fileLines = new(StringComparer.Ordinal);

    // A package is a key in project.assets.json, as in "NUnit/4.2.2", so "NUnit/ finds it and not NUnit3TestAdapter.
    static SearchValues<string> Packages(params string[] names) => SearchValues.Create(Array.ConvertAll(names, n => $"\"{n}/"), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?:Tests?|[._][A-Za-z]+)$", RegexOptions.IgnoreCase)]
    static partial Regex TestSuffix { get; }

    // <T>, with any type arguments nested in it, or coverlet's `1.
    [GeneratedRegex(@"(?<=\w)(?:<(?>[^<>]+|<(?<open>)|>(?<-open>))*(?(open)(?!))>|`\d+)")]
    static partial Regex TypeArguments { get; }

    [GeneratedRegex(@"<+([^<>]+)>")]
    static partial Regex OwnerName { get; }

    [GeneratedRegex(@"<[^<>]*>F[0-9A-F]+__")]
    static partial Regex FileLocalPrefix { get; }

    [GeneratedRegex(@"^(.*?)[./+](<.*)$")]
    static partial Regex NestedGenerated { get; }

    [GeneratedRegex(@"^\.c?ctor$")]
    static partial Regex Constructor { get; }

    // An init accessor compiles to set_.
    [GeneratedRegex("^(get|set)_Item$")]
    static partial Regex Indexer { get; }

    [GeneratedRegex(@"^(get|set|add|remove)_(.+)$")]
    static partial Regex Accessor { get; }
}

sealed record Source(string Assembly)
{
    public Dictionary<int, (int Hits, int Covered, int Branches)> Lines { get; } = new();
    public Dictionary<string, HashSet<int>> MemberLines { get; } = new(StringComparer.Ordinal);
}

sealed record Uses(Dictionary<string, int> Total, Dictionary<string, Dictionary<string, int>> Own, Dictionary<string, string> Entries, Dictionary<string, List<Site>>? Sites, string Root);

readonly record struct Site(string File, int Line, int Column, string? Qualifier, bool AfterNew, bool Assigned, bool InTests);

enum ProjectKind { Code, Test, Helper }
