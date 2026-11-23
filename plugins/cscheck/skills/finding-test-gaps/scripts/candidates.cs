#!/usr/bin/env dotnet
#:package Microsoft.CodeAnalysis.CSharp@5.0.0
#:property PublishAot=false
#:property Optimize=true
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property RunAnalyzers=false
#:property LangVersion=latest

// Lists the candidate mutants in C# files, of the kinds mutants.md ranks, and writes them where a mutants.cs spec names them by id:
// conditions and other bools forced, operands of &&, || and enum flags dropped, list, set and switch items dropped, statements that add
// to the result skipped, calls swapped for their siblings, like arguments swapped, values the code was given not passed on, boundaries
// moved, and null checks and fallbacks dropped. It finds the rest of Stryker.NET's mutations too, of operators, literals, jumps, bodies and
// regex patterns, but lists them only with --rest, since they found nothing new in the trials. Given coverage, it counts the candidates on
// lines no test runs, which can only survive, rather than listing them. Each is inside a member's body, or a [GeneratedRegex], with a find
// that occurs once. It doesn't judge them: which are worth a test is the sweep's work.
// The #:property lines stop a repo's Directory.Build.props from changing how this builds, when the skill is copied into a repo.
// dotnet run --file candidates.cs -- <file.cs> [<file.cs> ...] --out <candidates.json> [--members <Type.member,member>] [--coverage <results folder or .cobertura.xml>] [--root <repo>] [--rest]
// Each <file.cs> is relative to --root, the current folder by default; --out and --coverage are relative to the current folder.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

const string Usage = "usage: dotnet run --file candidates.cs -- <file.cs> [<file.cs> ...] --out <candidates.json> [--members <Type.member,member>] [--coverage <results folder or .cobertura.xml>] [--root <repo>] [--rest]\nEach <file.cs> is relative to --root, the current folder by default; --out and --coverage are relative to the current folder.";
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var files = new List<string>();
string? output = null;
string[]? members = null;
string? coverageFrom = null;
var withRest = false;
var root = Environment.CurrentDirectory;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "-h" or "--help")
    {
        Console.WriteLine(Usage);
        return 0;
    }
    if (!args[i].StartsWith("--", StringComparison.Ordinal))
    {
        files.Add(args[i]);
        continue;
    }
    if (args[i] == "--rest")
    {
        withRest = true;
        continue;
    }
    var (option, value) = (args[i], ++i < args.Length ? args[i] : "");
    if (option == "--out" && value.Length > 0) output = Path.GetFullPath(value);
    else if (option == "--members" && value.Length > 0) members = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    else if (option == "--root" && value.Length > 0) root = Path.GetFullPath(value);
    else if (option == "--coverage" && value.Length > 0) coverageFrom = Path.GetFullPath(value);
    else
    {
        Console.Error.WriteLine(option is "--out" or "--members" or "--root" or "--coverage" ? $"error: {option} takes a value" : $"error: there's no option {option}");
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
if (files.Count == 0 || output is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}
var coverage = coverageFrom is null ? null : ReadCoverage(coverageFrom);
if (coverageFrom is not null && coverage is null)
{
    Console.Error.WriteLine($"error: there's no .cobertura.xml at {coverageFrom}: pass the --results-directory folder or a .cobertura.xml file");
    return 1;
}

// Each file once, in order of its path, so the ids don't depend on how the files were named or in what order.
var paths = new SortedDictionary<string, string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
foreach (var name in files) paths.TryAdd(Path.GetFullPath(Path.Combine(root, name)), name);
var found = new List<Candidate>();
var declared = new List<(string FullType, string Member)>();
foreach (var (path, name) in paths)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"error: there's no {name}");
        return 1;
    }
    Dictionary<int, int>? hits = null;
    if (coverage is { } report)
    {
        if (!report.Lines.TryGetValue(path, out hits)) Console.WriteLine($"warning: {name} isn't in the coverage, so none of its candidates are marked as on lines no test runs");
        else if (File.GetLastWriteTimeUtc(path) > report.Written) Console.WriteLine($"warning: {name} changed after the coverage was written, so collect it again before trusting which lines run");
    }
    var finder = new Finder(path, Path.GetRelativePath(root, path).Replace('\\', '/'), hits);
    found.AddRange(finder.Find());
    declared.AddRange(finder.Members);
    if (finder.ParseErrors > 0) Console.WriteLine($"warning: {name} has {finder.ParseErrors} parts this Roslyn can't parse, such as newer C#, so candidates near them may be wrong or missing");
}
foreach (var member in members ?? [])
    if (!found.Exists(c => Finder.Matches(member, c.FullType, c.Member)))
        Console.WriteLine(declared.Exists(d => Finder.Matches(member, d.FullType, d.Member)) ? $"{member} has no candidates." : $"warning: there's no member named {member} in the files");

// A member's id is its name, or its type's and its name when two types in the files have it, with the type named as uncovered.cs names it.
var owners = found.Select(c => (c.File, c.FullType, c.Member)).ToHashSet().CountBy(o => o.Member).ToDictionary();
var types = found.Where(c => c.Type.Length > 0).Select(c => (c.FullType, c.Member)).ToHashSet().ToLookup(o => o.Member, o => o.FullType);
var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var mutants = new (Candidate Candidate, string Id, string Owner)[found.Count];
for (var k = 0; k < found.Count; k++)
{
    var candidate = found[k];
    var owner = candidate.Type.Length > 0 && owners[candidate.Member] > 1 ? $"{ShortType(candidate.FullType, types[candidate.Member])}.{candidate.Member}" : candidate.Member;
    var stem = NotIdChar.Replace($"{owner}.{candidate.Kind}", "-");
    var n = ++CollectionsMarshal.GetValueRefOrAddDefault(counts, stem, out _);
    mutants[k] = (candidate, $"{stem}{n}", owner);
}

// The file has every candidate, so ids don't change with --members and a spec's ids stay in it; --members only limits what's listed.
var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var lines = mutants.Select(m => "    " + JsonSerializer.Serialize(new { id = m.Id, bug = $"{m.Owner}: {m.Candidate.Description}", file = m.Candidate.File, find = m.Candidate.Find, replace = m.Candidate.Replace }, options));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, "{\n  \"mutants\": [\n" + string.Join(",\n", lines) + "\n  ]\n}\n");

var listed = mutants.Where(m => members is null || Array.Exists(members, name => Finder.Matches(name, m.Candidate.FullType, m.Candidate.Member))).ToArray();
// Each member's candidates, with how many are on lines no test runs, and how many of the rest of Stryker.NET's are on lines that run.
var groups = listed.GroupBy(m => (m.Candidate.File, m.Owner)).Select(g => (g.Key, Mutants: g, NotRun: g.Count(m => m.Candidate.NotRun), OfRest: g.Count(m => m.Candidate.Rest && !m.Candidate.NotRun))).ToArray();
static string Place((string File, string Owner) key, IEnumerable<(Candidate Candidate, string Id, string Owner)> group, int files) =>
    $"{(files > 1 ? key.File + ", " : "")}lines {group.Min(m => m.Candidate.Line)}-{group.Max(m => m.Candidate.Line)}";
// What's listed is what a sweep runs.
var notRun = groups.Sum(g => g.NotRun);
var hidden = withRest ? 0 : groups.Sum(g => g.OfRest);
var shown = listed.Length - notRun - hidden;
var omitted = notRun > 0 ? $"{notRun} on lines no test runs" : "";
if (hidden > 0) omitted += $"{(omitted.Length > 0 ? " and " : "")}{hidden} of the rest of Stryker.NET's";
Console.WriteLine($"Wrote {found.Count} candidates to {output}{(members is null ? "" : $", {listed.Length} of them in the members named")}. It lists {shown}{(omitted.Length > 0 ? $", leaving out {omitted}" : "")}.");
if (coverage is { } seen) Console.WriteLine($"The lines no test runs are as the coverage written {seen.Written.ToLocalTime():yyyy-MM-dd HH:mm} saw them, so tests written since then don't count.");
if (members is null && shown > 150)
{
    Console.WriteLine("That's too many to show here, so these are the members with their counts. Name some with --members to list theirs.");
    foreach (var group in groups)
        Console.WriteLine($"  {group.Key.Owner} ({Place(group.Key, group.Mutants, paths.Count)}): {group.Mutants.Count() - group.OfRest - group.NotRun}{(group.OfRest > 0 ? $", and {group.OfRest} of the rest of Stryker.NET's" : "")}{(group.NotRun > 0 ? $", and {group.NotRun} on lines no test runs" : "")}");
    return 0;
}
var width = listed.Length > 0 ? listed.Max(m => m.Id.Length) : 0;
foreach (var group in groups)
{
    Console.WriteLine($"{group.Key.Owner} ({Place(group.Key, group.Mutants, paths.Count)})");
    Section(group.Mutants.Where(m => !m.Candidate.Rest && !m.Candidate.NotRun), null);
    if (withRest) Section(group.Mutants.Where(m => m.Candidate.Rest && !m.Candidate.NotRun), "The rest of Stryker.NET's, which found nothing new in the trials:");
    var rest = withRest ? 0 : group.OfRest;
    var leftOut = group.NotRun > 0 ? $"{group.NotRun} on lines no test runs, which can only survive" : "";
    if (rest > 0) leftOut += $"{(leftOut.Length > 0 ? ", and " : "")}{rest} of the rest of Stryker.NET's, which --rest lists";
    if (leftOut.Length > 0) Console.WriteLine($"  Left out: {leftOut}.");
}
return 0;

void Section(IEnumerable<(Candidate Candidate, string Id, string Owner)> section, string? heading)
{
    foreach (var (candidate, id, _) in section)
    {
        if (heading is not null) Console.WriteLine($"  {heading}");
        heading = null;
        Console.WriteLine($"  {id.PadRight(width)} {candidate.Line,5}  {candidate.Description}");
    }
}

// Each file's lines with the most hits any report gave them, from every cobertura report under a folder, as uncovered.cs merges them.
static (Dictionary<string, Dictionary<int, int>> Lines, DateTime Written)? ReadCoverage(string path)
{
    FileInfo[] reports = Directory.Exists(path) ? new DirectoryInfo(path).GetFiles("*.cobertura.xml", SearchOption.AllDirectories) : File.Exists(path) ? [new FileInfo(path)] : [];
    if (reports.Length == 0) return null;
    var lines = new Dictionary<string, Dictionary<int, int>>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    foreach (var report in reports)
    {
        var xml = XDocument.Load(report.FullName).Root;
        // coverlet and ReportGenerator name files relative to the report's source folders.
        var folders = xml?.Element("sources")?.Elements("source").Select(s => s.Value.Trim()).ToArray() ?? [];
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in xml?.Element("packages")?.Elements("package").SelectMany(p => p.Element("classes")?.Elements("class") ?? []) ?? [])
        {
            if ((string?)type.Attribute("filename") is not { } filename) continue;
            if (!Path.IsPathRooted(filename)) filename = CollectionsMarshal.GetValueRefOrAddDefault(resolved, filename, out _) ??= folders.Select(f => Path.GetFullPath(Path.Combine(f, filename))).FirstOrDefault(File.Exists) ?? filename;
            var hits = CollectionsMarshal.GetValueRefOrAddDefault(lines, filename, out _) ??= [];
            foreach (var line in type.Element("lines")?.Elements("line") ?? [])
            {
                ref var most = ref CollectionsMarshal.GetValueRefOrAddDefault(hits, (int)line.Attribute("number")!, out _);
                most = Math.Max(most, (int)line.Attribute("hits")!);
            }
        }
    }
    return (lines, reports.Max(r => r.LastWriteTimeUtc));
}

// The fewest of a type's trailing names that no other of the types ends with, so GenInt.Range and GenLong.Range stay apart.
static string ShortType(string type, IEnumerable<string> types)
{
    for (var dot = type.LastIndexOf('.'); dot > 0; dot = type.LastIndexOf('.', dot - 1))
    {
        var tail = type[(dot + 1)..];
        if (!types.Any(other => other != type && Finder.IsTypeNamed(other, tail))) return tail;
    }
    return type;
}

partial class Program
{
    [GeneratedRegex(@"[^\w.-]")]
    static partial Regex NotIdChar { get; }
}

sealed record Candidate(string File, string Type, string FullType, string Member, string Kind, int Line, string Find, string Replace, string Description, bool Rest, bool NotRun);

sealed partial class Finder(string path, string file, Dictionary<int, int>? hits)
{
    // Code that #if leaves out of a build of current .NET isn't in the tree, so it gets no candidates.
    static readonly string[] _defines = ["TRACE", "RELEASE", "NET", "NETCOREAPP", "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER", "NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET7_0_OR_GREATER", "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0_OR_GREATER", "NET11_0_OR_GREATER"];

    // Logging, tracing, metrics, sleeps, cancellation, assertions and disposal, by the names that give them away. A name after a dot is
    // another type's member, such as a Trace property.
    [GeneratedRegex(@"(?<!\.)\b(?:_?[Ll]ogger|_?[Ll]og|Console|Debug|Trace|Activity|Telemetry|Metrics?)\.|\.Log(?:Information|Warning|Error|Debug|Trace|Critical)\(|\b(?:Thread\.Sleep|Task\.Delay|ThrowIfCancellationRequested|IsCancellationRequested|Dispose|DisposeAsync)\b|\bAssert\.")]
    static partial Regex Noise { get; }

    static readonly Dictionary<string, string[]> _siblings = new()
    {
        { "Any", ["All"] },
        { "All", ["Any"] },
        { "Min", ["Max"] },
        { "Max", ["Min"] },
        { "MinBy", ["MaxBy"] },
        { "MaxBy", ["MinBy"] },
        { "First", ["Last"] },
        { "Last", ["First"] },
        { "FirstOrDefault", ["LastOrDefault", "First"] },
        { "LastOrDefault", ["FirstOrDefault", "Last"] },
        { "Single", ["SingleOrDefault"] },
        { "SingleOrDefault", ["Single"] },
        { "Floor", ["Ceiling"] },
        { "Ceiling", ["Floor"] },
        { "Round", ["Floor"] },
        { "AddDays", ["AddMonths"] },
        { "AddMonths", ["AddDays"] },
        { "AddHours", ["AddMinutes"] },
        { "AddMinutes", ["AddHours"] },
        { "StartsWith", ["EndsWith"] },
        { "EndsWith", ["StartsWith"] },
        { "TrimStart", ["TrimEnd"] },
        { "TrimEnd", ["TrimStart"] },
        { "PadLeft", ["PadRight"] },
        { "PadRight", ["PadLeft"] },
        { "IndexOf", ["LastIndexOf"] },
        { "LastIndexOf", ["IndexOf"] },
        { "OrderBy", ["OrderByDescending"] },
        { "OrderByDescending", ["OrderBy"] },
        { "ThenBy", ["ThenByDescending"] },
        { "ThenByDescending", ["ThenBy"] },
        { "Skip", ["Take"] },
        { "Take", ["Skip"] },
        { "Union", ["Intersect"] },
        { "Intersect", ["Union"] },
        { "EnqueueDequeue", ["DequeueEnqueue"] },
        { "DequeueEnqueue", ["EnqueueDequeue"] },
        { "ToLocalTime", ["ToUniversalTime"] },
        { "ToUniversalTime", ["ToLocalTime"] },
        { "GetUtcNow", ["GetLocalNow"] },
        { "GetLocalNow", ["GetUtcNow"] },
    };

    // The rest of Stryker.NET's LINQ and string swaps.
    static readonly Dictionary<string, string[]> _strykerSiblings = new()
    {
        { "First", ["FirstOrDefault"] },
        { "Order", ["OrderDescending"] },
        { "OrderDescending", ["Order"] },
        { "SkipWhile", ["TakeWhile"] },
        { "TakeWhile", ["SkipWhile"] },
        { "SkipLast", ["TakeLast"] },
        { "TakeLast", ["SkipLast"] },
        { "Concat", ["Except"] },
        { "Except", ["Concat"] },
        { "Append", ["Prepend"] },
        { "Prepend", ["Append"] },
        { "AsEnumerable", ["Reverse"] },
        { "Reverse", ["AsEnumerable"] },
        { "Average", ["Min"] },
        { "Sum", ["Max"] },
        { "ToLower", ["ToUpper"] },
        { "ToUpper", ["ToLower"] },
        { "ToLowerInvariant", ["ToUpperInvariant"] },
        { "ToUpperInvariant", ["ToLowerInvariant"] },
    };

    // The rest of Stryker.NET's kinds, which found nothing new in the trials, so they're listed apart.
    static readonly HashSet<string> _restKinds = ["arithmetic", "bitwise", "assignment", "equality", "update", "unary", "negation", "logical", "checked", "string", "coalesce", "collection", "removal", "block", "regex"];

    // Properties and enum members, which are read, not called.
    static readonly Dictionary<string, string> _propertySiblings = new()
    {
        { "Now", "UtcNow" },
        { "UtcNow", "Now" },
        { "LocalDateTime", "UtcDateTime" },
        { "UtcDateTime", "LocalDateTime" },
        { "AwayFromZero", "ToEven" },
        { "ToEven", "AwayFromZero" },
    };

    // Stryker.NET's Math swaps. Log goes to Exp with one argument and to Pow with two.
    static readonly Dictionary<string, string[]> _mathSiblings = new()
    {
        { "Acos", ["Acosh", "Asin", "Atan"] },
        { "Acosh", ["Acos", "Asinh", "Atanh"] },
        { "Asin", ["Asinh", "Acos", "Atan"] },
        { "Asinh", ["Asin", "Acosh", "Atanh"] },
        { "Atan", ["Atanh", "Acos", "Asin"] },
        { "Atanh", ["Atan", "Acosh", "Asinh"] },
        { "BitDecrement", ["BitIncrement"] },
        { "BitIncrement", ["BitDecrement"] },
        { "Cos", ["Cosh", "Sin", "Tan"] },
        { "Cosh", ["Cos", "Sinh", "Tanh"] },
        { "Exp", ["Log"] },
        { "MaxMagnitude", ["MinMagnitude"] },
        { "MinMagnitude", ["MaxMagnitude"] },
        { "Pow", ["Log"] },
        { "ReciprocalEstimate", ["ReciprocalSqrtEstimate"] },
        { "ReciprocalSqrtEstimate", ["ReciprocalEstimate", "Sqrt"] },
        { "Sin", ["Sinh", "Cos", "Tan"] },
        { "Sinh", ["Sin", "Cosh", "Tanh"] },
        { "Tan", ["Tanh", "Cos", "Sin"] },
        { "Tanh", ["Tan", "Cosh", "Sinh"] },
    };

    // Operators' names in IL, which the coverage and so uncovered.cs give them.
    static readonly Dictionary<string, string> _operators = new()
    {
        { "+", "Addition" }, { "-", "Subtraction" }, { "*", "Multiply" }, { "/", "Division" }, { "%", "Modulus" },
        { "&", "BitwiseAnd" }, { "|", "BitwiseOr" }, { "^", "ExclusiveOr" }, { "<<", "LeftShift" }, { ">>", "RightShift" }, { ">>>", "UnsignedRightShift" },
        { "==", "Equality" }, { "!=", "Inequality" }, { "<", "LessThan" }, { ">", "GreaterThan" }, { "<=", "LessThanOrEqual" }, { ">=", "GreaterThanOrEqual" },
        { "!", "LogicalNot" }, { "~", "OnesComplement" }, { "++", "Increment" }, { "--", "Decrement" }, { "true", "True" }, { "false", "False" },
        { "+=", "AdditionAssignment" }, { "-=", "SubtractionAssignment" }, { "*=", "MultiplicationAssignment" }, { "/=", "DivisionAssignment" },
        { "%=", "ModulusAssignment" }, { "&=", "BitwiseAndAssignment" }, { "|=", "BitwiseOrAssignment" }, { "^=", "ExclusiveOrAssignment" },
        { "<<=", "LeftShiftAssignment" }, { ">>=", "RightShiftAssignment" }, { ">>>=", "UnsignedRightShiftAssignment" },
    };

    readonly List<Candidate> _found = [];
    readonly HashSet<(int, int, string)> _seen = [];
    readonly Dictionary<TextSpan, bool> _quiet = [];
    string _text = "";
    TextLineCollection _lines = SourceText.From("").Lines;
    bool _latin1;

    public int ParseErrors { get; private set; }

    // Every member uncovered.cs can name, so a member named with --members that has nothing to mutate isn't taken for a misspelling.
    public List<(string FullType, string Member)> Members { get; } = [];

    public List<Candidate> Find()
    {
        var (text, encoding) = ReadSource(path);
        _text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        _latin1 = encoding.CodePage == Encoding.Latin1.CodePage;
        // FileBasedProgram takes a file-based app's #! and #: lines, which are errors without it.
        var tree = CSharpSyntaxTree.ParseText(_text, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular, _defines).WithFeatures([new("FileBasedProgram", "true")]));
        // Roslyn's lines, which are the PDB's and so the coverage's, even in a file with a lone CR or a Unicode line break.
        _lines = tree.GetText().Lines;
        ParseErrors = tree.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
        // A class, record or struct has a constructor even when it declares none, where its initializers run.
        foreach (var node in tree.GetRoot().DescendantNodes())
            if (node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax) Members.Add((FullType(node), "ctor"));
            else if (node is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax && Owner(node) is { Member.Length: > 0 } owner) Members.Add((owner.FullType, owner.Member));
        foreach (var node in tree.GetRoot().DescendantNodes(SearchInside))
        {
            if (node is AttributeListSyntax attributes)
            {
                GeneratedRegexes(attributes);
                continue;
            }
            if (node is BlockSyntax or ArrowExpressionClauseSyntax && node.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax) Body(node);
            if (!InBody(node)) continue;
            if (node is ExpressionSyntax whole && IsWholeBool(whole)) Force(whole, both: true, kind: "bool");
            // A loop forced true never ends, which only costs a timeout.
            if (ConditionOf(node).Condition is { } condition) Force(condition, both: node is not (WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax));
            switch (node)
            {
                case IfStatementSyntax { Else: { Statement: IfStatementSyntax inner } otherwise }:
                    Add(otherwise, "item", otherwise.Span, inner.Else?.ToString() ?? "", $"drops `else if ({Brief(inner.Condition)})`");
                    break;
                case BinaryExpressionSyntax b: Binary(b); break;
                case BinaryPatternSyntax p when p.Parent is not BinaryPatternSyntax outer || !outer.IsKind(p.Kind()):
                    DropOperands(p, $" {p.OperatorToken.Text} ");
                    Flip(p);
                    break;
                case RelationalPatternSyntax r: Boundary(r, r.OperatorToken); break;
                case CollectionExpressionSyntax c: Collection(c); break;
                case InitializerExpressionSyntax i: Initializer(i); break;
                case SwitchSectionSyntax s: Section(s); break;
                case SwitchExpressionSyntax s when s.Arms.Count > 1: DropItems(s.Arms, arm => arm.Pattern is DiscardPatternSyntax ? null : $"the arm `{Brief(arm.Pattern)} =>`"); break;
                case ExpressionStatementSyntax e: Statement(e); break;
                case InvocationExpressionSyntax i: Invocation(i); break;
                case BaseObjectCreationExpressionSyntax { ArgumentList: { } arguments } creation:
                    SwapArguments(creation, arguments, "new");
                    PassedOn(creation, arguments);
                    if (IsRegex(creation) && arguments.Arguments is [{ Expression: LiteralExpressionSyntax pattern }, ..]) RegexPattern(creation, pattern);
                    break;
                case MemberAccessExpressionSyntax m: MemberAccess(m); break;
                case ConditionalAccessExpressionSyntax c: Add(c, "null", c.OperatorToken.Span, "", $"`{Brief(c)}` without its null check"); break;
                case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.TrueLiteralExpression) || l.IsKind(SyntaxKind.FalseLiteralExpression):
                    var flipped = l.IsKind(SyntaxKind.TrueLiteralExpression) ? "false" : "true";
                    Add(l, "flag", l.Span, flipped, $"`{l}` becomes `{flipped}`");
                    break;
                case ReturnStatementSyntax { Expression: LiteralExpressionSyntax { Token.Value: int value } literal } r when r.Ancestors().TakeWhile(a => a is not MemberDeclarationSyntax and not LocalFunctionStatementSyntax).Any(a => a is IfStatementSyntax or SwitchSectionSyntax) && literal.Token.Text.All(char.IsAsciiDigit):
                    Add(literal, "return", literal.Span, (value + 1).ToString(CultureInfo.InvariantCulture), $"an early return gives {value + 1}, not {value}");
                    break;
                case PrefixUnaryExpressionSyntax u: Unary(u); break;
                case PostfixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.PostIncrementExpression) || u.IsKind(SyntaxKind.PostDecrementExpression): Update(u, u.OperatorToken); break;
                case AssignmentExpressionSyntax a when !a.IsKind(SyntaxKind.SimpleAssignmentExpression): Assignment(a); break;
                case IsPatternExpressionSyntax p: IsPattern(p); break;
                case CheckedExpressionSyntax c when c.IsKind(SyntaxKind.CheckedExpression): Add(c, "checked", c.Span, $"({c.Expression})", $"`{Brief(c)}` without `checked`"); break;
                case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) || l.IsKind(SyntaxKind.Utf8StringLiteralExpression): StringLiteral(l); break;
                case InterpolatedStringExpressionSyntax s when s.Contents.Count > 0 && !IsMessage(s): Add(s, "string", s.Span, "$\"\"", $"`{Brief(s)}` becomes `$\"\"`"); break;
                case BreakStatementSyntax or ContinueStatementSyntax or YieldStatementSyntax or GotoStatementSyntax: Jump((StatementSyntax)node); break;
            }
        }
        return [.. _found.OrderBy(c => c.Line)];
    }

    // A condition, or a bool that's returned or assigned rather than tested, forced true and false. One that declares a variable, such as is
    // Foo f or out var v, is forced only if no code outside it uses the variable. If only the code it guards does, it's forced false as
    // false && (...), which keeps the variable for code that then can't run.
    void Force(ExpressionSyntax condition, bool both, string kind = "cond")
    {
        if (condition is LiteralExpressionSyntax) return;
        var never = "false";
        if (Declares(condition))
        {
            var (guarded, elsewhere) = Uses(condition);
            if (elsewhere || AssignsOut(condition)) return;
            if (guarded) (both, never) = (false, $"false && ({Text(condition.Span)})");
        }
        if (both) Add(condition, kind, condition.Span, "true", $"`{Brief(condition)}` is always true");
        Add(condition, kind, condition.Span, never, $"`{Brief(condition)}` is always false");
    }

    // Where the variables a condition declares are used outside it: in the code it guards, which runs only when it's true, or anywhere else
    // in the scope they can reach, counting any name that matches.
    static (bool Guarded, bool Elsewhere) Uses(ExpressionSyntax condition)
    {
        var names = condition.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>().Select(d => d.Identifier.ValueText).ToHashSet();
        var guards = ConditionOf(condition.Parent).Guards;
        // Top-level statements share one scope.
        var scope = condition.Ancestors().First(a => a is MemberDeclarationSyntax and not GlobalStatementSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or CompilationUnitSyntax);
        var (guarded, elsewhere) = (false, false);
        foreach (var use in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!names.Contains(use.Identifier.ValueText) || condition.Span.Contains(use.Span)) continue;
            if (guards is { } span && span.Contains(use.Span)) guarded = true;
            else elsewhere = true;
        }
        return (guarded, elsewhere);
    }

    // A bool that's a whole value: returned, an expression body, assigned or initialized, though not where it declares a variable that the
    // code after it may use, or assigns an out argument.
    static bool IsWholeBool(ExpressionSyntax e) => e is not LiteralExpressionSyntax && IsBool(e) && !AssignsOut(e) && e.Parent switch
    {
        ReturnStatementSyntax or ArrowExpressionClauseSyntax => true,
        AssignmentExpressionSyntax { Parent: ExpressionStatementSyntax } a => a.IsKind(SyntaxKind.SimpleAssignmentExpression) && a.Right == e && !Declares(e),
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax or PropertyDeclarationSyntax } => !Declares(e),
        _ => false,
    };

    void Binary(BinaryExpressionSyntax b)
    {
        switch (b.Kind())
        {
            case SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression when b.Parent is not BinaryExpressionSyntax outer || !outer.IsKind(b.Kind()):
                DropOperands(b, $" {b.OperatorToken.Text} ");
                Flip(b);
                break;
            case SyntaxKind.SubtractExpression:
                Add(b, "swap", b.Span, $"{Wrap(b.Right)} - {Wrap(b.Left)}", $"`{Brief(b)}` subtracts the other way round");
                break;
            case SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression:
                Boundary(b, b.OperatorToken);
                break;
            case SyntaxKind.DivideExpression when b.Right is LiteralExpressionSyntax { Token.Value: double or float or decimal } divisor
                && Convert.ToDouble(divisor.Token.Value, CultureInfo.InvariantCulture) is var whole && whole == Math.Floor(whole) && whole is >= 1 and < 1e9:
                Add(b, "division", divisor.Span, ((long)whole).ToString(CultureInfo.InvariantCulture), $"`{Brief(b)}` divides by an integer, so an integer numerator loses its fraction");
                break;
            // A value left alone in parentheses is wrapped again, since (x)! and (x)() read as casts.
            case SyntaxKind.CoalesceExpression when b.Parent is ParenthesizedExpressionSyntax:
                Add(b, "null", b.Span, $"({b.Left})", $"`{Brief(b)}` without its `??` fallback");
                break;
            case SyntaxKind.CoalesceExpression:
                Add(b, "null", TextSpan.FromBounds(b.Left.Span.End, b.Span.End), "", $"`{Brief(b)}` without its `??` fallback");
                break;
            // Enum flags such as RegexOptions.IgnoreCase | RegexOptions.Compiled, each left out in turn.
            case SyntaxKind.BitwiseOrExpression when b.Parent is not BinaryExpressionSyntax outer || !outer.IsKind(SyntaxKind.BitwiseOrExpression):
                if (Array.TrueForAll(Operands(b), f => f is MemberAccessExpressionSyntax)) DropOperands(b, " | ");
                break;
        }
        Operator(b);
    }

    // A comparison's < or > moved to or from <= or >=, in an expression or a pattern such as is < 12.
    void Boundary(SyntaxNode comparison, SyntaxToken operatorToken)
    {
        var moved = operatorToken.Text switch { "<" => "<=", "<=" => "<", ">" => ">=", _ => ">" };
        Add(comparison, "boundary", operatorToken.Span, moved, $"`{Brief(comparison)}` uses `{moved}`, which differs only where the two sides are equal");
    }

    // Stryker.NET's swaps of an operator. A comparison that's a whole condition or an operand of && or || isn't swapped, since forcing the
    // condition or dropping the operand already shows what the swap would. & and | bind differently, as do ^ and ==, so those keep their
    // operands grouped.
    void Operator(BinaryExpressionSyntax b)
    {
        var whole = Forced(b);
        switch (b.Kind())
        {
            case SyntaxKind.AddExpression when !IsText(b.Left) && !IsText(b.Right): Swap("arithmetic", "-"); break;
            case SyntaxKind.SubtractExpression: Swap("arithmetic", "+"); break;
            case SyntaxKind.MultiplyExpression: Swap("arithmetic", "/"); break;
            case SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression: Swap("arithmetic", "*"); break;
            case SyntaxKind.EqualsExpression when !whole: Swap("equality", "!="); break;
            case SyntaxKind.NotEqualsExpression when !whole: Swap("equality", "=="); break;
            case SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression when !whole: Swap("equality", ">"); break;
            case SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression when !whole: Swap("equality", "<"); break;
            case SyntaxKind.LeftShiftExpression: Swap("bitwise", ">>"); break;
            case SyntaxKind.RightShiftExpression: Swap("bitwise", "<<"); break;
            case SyntaxKind.BitwiseAndExpression: Group("bitwise", "|", ""); break;
            case SyntaxKind.BitwiseOrExpression: Group("bitwise", "&", ""); break;
            // ^ on bools becomes ==, and on integers is complemented; an operand that shows which it is rules out the other.
            case SyntaxKind.ExclusiveOrExpression:
                var integers = IsInteger(b.Left) || IsInteger(b.Right);
                var bools = IsBool(b.Left) || IsBool(b.Right);
                if (!integers) Group("logical", "==", ", for bools");
                if (!bools) Add(b, "bitwise", b.Span, $"~({b})", $"`{Brief(b)}` is complemented, for integers");
                break;
            // A fallback of throw, [], default or null can't stand alone, and one that can't be null can't go first.
            case SyntaxKind.CoalesceExpression when b.Right is not (ThrowExpressionSyntax or CollectionExpressionSyntax or LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression or (int)SyntaxKind.NullLiteralExpression }):
                // Left alone in parentheses, the fallback is wrapped again, since (f)() and (x)! read as casts.
                var fallback = b.Parent is ParenthesizedExpressionSyntax ? $"({b.Right})" : b.Right.ToString();
                if (Origin(b.Left) is { } given && Given(given) is not null) Add(b, "argument", b.Span, fallback, $"`{Brief(b)}` is always its fallback, so the `{given}` it was given is ignored");
                else Add(b, "coalesce", b.Span, fallback, $"`{Brief(b)}` is always its fallback");
                if (!IsValue(b.Right)) Add(b, "coalesce", b.Span, $"{Wrap(b.Right)} ?? {Wrap(b.Left)}", $"`{Brief(b)}` tries its fallback first");
                break;
        }

        void Swap(string kind, string other) => Add(b, kind, b.OperatorToken.Span, Apart(b.OperatorToken.Span, other), $"`{Brief(b)}` uses `{other}`");
        void Group(string kind, string other, string note) => Add(b, kind, b.Span, $"({Wrap(b.Left)} {other} {Wrap(b.Right)})", $"`{Brief(b)}` uses `{other}`{note}");
    }

    // && for || and back in a chain of three or more, as Stryker.NET swaps them, grouped as the chain was: in a chain of two, dropping each
    // operand already shows what a swap would. A chain that declares or assigns a variable is left alone, since the swap would leave it
    // unassigned.
    void Flip(SyntaxNode chain)
    {
        if (Operands(chain).Length < 3 || Declares(chain) || chain.DescendantNodes().Any(n => n is AssignmentExpressionSyntax)) return;
        var other = chain.Kind() switch
        {
            SyntaxKind.LogicalAndExpression => "||",
            SyntaxKind.LogicalOrExpression => "&&",
            SyntaxKind.AndPattern => "or",
            _ => "and",
        };
        foreach (var node in chain.DescendantNodesAndSelf(n => n.IsKind(chain.Kind())).Where(n => n.IsKind(chain.Kind())))
        {
            SyntaxNode left, right;
            if (node is BinaryExpressionSyntax b) (left, right) = (b.Left, b.Right);
            else (left, right) = (((BinaryPatternSyntax)node).Left, ((BinaryPatternSyntax)node).Right);
            // Two constants or types joined by and can never match, which C# rejects, so or becomes and only between ranges such as > 0.
            if (other == "and" && (left is not RelationalPatternSyntax || right is not RelationalPatternSyntax)) continue;
            Add(node, "logical", node.Span, $"({Grouped(left)} {other} {Grouped(right)})", $"`{Brief(node)}` uses `{other}`");
        }

        // An operand that's itself a chain keeps its own grouping under the other operator.
        static string Grouped(SyntaxNode operand) => operand.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.AndPattern or SyntaxKind.OrPattern
            ? $"({operand})"
            : operand.ToString();
    }

    // Each operand of a chain of one operator dropped in turn, such as one item from x == A || x == B || x == C.
    void DropOperands(SyntaxNode chain, string separator)
    {
        var operands = Operands(chain);
        for (var k = 0; k < operands.Length; k++)
        {
            if (Declares(operands[k])) continue;
            var kept = string.Join(separator, operands.Where((_, j) => j != k).Select(o => o.ToString()));
            Add(chain, "operand", chain.Span, kept, $"`{Brief(chain)}` without `{Brief(operands[k])}`");
        }
    }

    static SyntaxNode[] Operands(SyntaxNode chain) => [.. chain.DescendantNodes(n => n.IsKind(chain.Kind())).Where(n => !n.IsKind(chain.Kind()))];

    void Initializer(InitializerExpressionSyntax i)
    {
        // The values in an element such as { key, value } are the arguments of one Add, so dropping one of them doesn't compile.
        if (i.IsKind(SyntaxKind.ComplexElementInitializerExpression)) return;
        var assigns = i.IsKind(SyntaxKind.ObjectInitializerExpression) || i.IsKind(SyntaxKind.WithInitializerExpression);
        // One item, which isn't dropped below, and a long list, whose items are only sampled, are emptied, as Stryker.NET does. An
        // implicitly typed array needs an item to have a type.
        if (!assigns && i.Expressions.Count is 1 or > 10 && i.Parent is not (ImplicitArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax))
            Add(i, "collection", i.Span, "{ }", $"`{Brief(i)}` is empty");
        if (!assigns && i.Expressions.Count < 2) return;
        var from = !assigns ? "" : i.IsKind(SyntaxKind.WithInitializerExpression) ? " from the with" : " from the initializer";
        // Entries such as ["key"] = value make a table, as a collection's items do, where an object's properties each have a job of their own.
        var table = !assigns || i.Expressions.All(e => e is AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax });
        DropItems(i.Expressions, e => $"`{Brief(e)}`{from}", sampled: table);
    }

    // An empty collection given an item, and one of one item, or of more than Picked samples, emptied, as Stryker.NET does.
    void Collection(CollectionExpressionSyntax c)
    {
        // A dictionary's collection expression takes no single item.
        if (c.Elements.Count == 0 && !DeclaredType(c).Contains("Dictionary", StringComparison.Ordinal)) Add(c, "collection", c.Span, "[default]", "`[]` gets a default item");
        if (c.Elements.Count > 1) DropItems(c.Elements, e => $"`{Brief(e)}`");
        if (c.Elements.Count is 1 or > 10) Add(c, "collection", c.Span, "[]", $"`{Brief(c)}` is empty");
    }

    // Each item dropped in turn, except one described as null, and in a sampled list of more than 10 only the ones Picked samples.
    void DropItems<T>(SeparatedSyntaxList<T> items, Func<T, string?> describe, bool sampled = true) where T : SyntaxNode
    {
        for (var k = 0; k < items.Count; k++)
            if ((!sampled || Picked(k, items.Count)) && describe(items[k]) is { } item) Add(items[k], "item", Removal(items, k), "", $"drops {item}{(sampled ? OneOf(items.Count) : "")}");
    }

    // A long list or table is a test over every entry's job, so only its first, middle and last items are dropped.
    static bool Picked(int k, int count) => count <= 10 || k == 0 || k == count / 2 || k == count - 1;

    static string OneOf(int count) => count > 10 ? $", one of {count}" : "";

    // A label of several is dropped alone; a case of its own is dropped with its statements, so its values go to the default. A section that
    // only logs and the like isn't.
    void Section(SwitchSectionSyntax s)
    {
        if (s.Parent is SwitchStatementSyntax { Sections: var sections } && !Picked(sections.IndexOf(s), sections.Count) || OnlyNoise(s.Statements)) return;
        if (s.Labels is [not DefaultSwitchLabelSyntax]) Add(s, "item", s.Span, "", $"drops the `{Brief(s.Labels[0])}` section");
        else
            foreach (var label in s.Labels)
                if (label is not DefaultSwitchLabelSyntax) Add(label, "item", label.Span, "", $"drops `{Brief(label)}`");
    }

    void Statement(ExpressionStatementSyntax e)
    {
        if (Acts(e.Expression)) Skip(e, "statement");
    }

    // A call, an assignment, ++ or --, after any await and ?., which is what a statement or an => body that returns nothing does.
    static bool Acts(ExpressionSyntax expression)
    {
        if (expression is AwaitExpressionSyntax awaited) expression = awaited.Expression;
        while (expression is ConditionalAccessExpressionSyntax c) expression = c.WhenNotNull;
        return expression is InvocationExpressionSyntax or AssignmentExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax;
    }

    // A statement that's an if's or a loop's whole body becomes an empty one, so the next statement doesn't take its place.
    void Skip(StatementSyntax s, string kind) => Add(s, kind, s.Span, s.Parent is BlockSyntax or SwitchSectionSyntax or GlobalStatementSyntax ? "" : ";", $"skips `{Brief(s)}`");

    void Invocation(InvocationExpressionSyntax i)
    {
        var name = i.Expression switch { MemberAccessExpressionSyntax m => m.Name, MemberBindingExpressionSyntax b => b.Name, SimpleNameSyntax s => s, _ => null };
        if (name is null) return;
        var called = name.Identifier.ValueText;
        var count = i.ArgumentList.Arguments.Count;
        var receiver = i.Expression is MemberAccessExpressionSyntax access ? access.Expression.ToString() : "";
        // After a ?., the receiver starts with the member it binds, such as the .Name of x?.Name.Trim(), which must stay first.
        var bound = receiver is ['.' or '[', ..];
        if (!Unshared(i, called, receiver, count))
        {
            foreach (var sibling in _siblings.GetValueOrDefault(called) ?? []) Add(i, "sibling", name.Identifier.Span, sibling, $"`{called}` becomes `{sibling}`");
            foreach (var sibling in _strykerSiblings.GetValueOrDefault(called) ?? []) Add(i, "sibling", name.Identifier.Span, sibling, $"`{called}` becomes `{sibling}`", rest: true);
        }
        // Math's own are swapped only on Math and the floating-point types, since names such as Log and Sin mean other things elsewhere.
        if (receiver is "Math" or "MathF" or "System.Math" or "System.MathF" or "double" or "float" or "Double" or "Single")
        {
            string[] maths = called == "Log" ? count switch { 1 => ["Exp"], 2 => ["Pow"], _ => [] } : _mathSiblings.GetValueOrDefault(called) ?? [];
            foreach (var sibling in maths) Add(i, "sibling", name.Identifier.Span, sibling, $"`{called}` becomes `{sibling}`", rest: true);
        }
        // Where and Distinct keep the element type, so the rest of the chain still compiles without them.
        if (called is "Where" or "Distinct" && i.Expression is MemberAccessExpressionSyntax filtered)
            Add(i, "filter", TextSpan.FromBounds(filtered.Expression.Span.End, i.Span.End), "", $"drops `.{called}({Brief(i.ArgumentList.Arguments.Span)})`");
        if (called == "CompareTo" && count == 1 && !bound && i.Expression is MemberAccessExpressionSyntax compared && i.ArgumentList.Arguments[0] is { NameColon: null } other)
            Add(i, "swap", i.Span, $"{Wrap(other.Expression)}.CompareTo({compared.Expression})", $"`{Brief(i)}` compares the other way round");
        SwapArguments(i, i.ArgumentList, called);
        PassedOn(i, i.ArgumentList);
        Strings(i, called, receiver, count, bound);
        if (receiver is "Regex" or "System.Text.RegularExpressions.Regex" && count > 1 && called is "IsMatch" or "Match" or "Matches" or "Replace" or "Split" or "Count" or "EnumerateMatches" or "EnumerateSplits"
            && i.ArgumentList.Arguments[1].Expression is LiteralExpressionSyntax pattern)
            RegexPattern(i, pattern);
    }

    // Siblings without the call's overloads: Any, Round and Except have overloads their siblings lack, so they're swapped only with the
    // one argument both take, string.Concat, Observable.Concat and Array.Reverse have no Except or AsEnumerable, and a StringBuilder's
    // Append has no Prepend.
    static bool Unshared(InvocationExpressionSyntax i, string called, string receiver, int count) => called switch
    {
        "Any" or "Round" or "Except" => count != 1,
        "Concat" or "Reverse" => receiver is "string" or "String" or "Observable" or "Array" or "MemoryExtensions",
        "Append" => Builds(i),
        _ => false,
    };

    // A StringBuilder's Append: a statement, or chained to another builder call, where a sequence's Append would be thrown away.
    static bool Builds(InvocationExpressionSyntax i) =>
        i.Parent is ExpressionStatementSyntax
        || i.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var next } && (next.StartsWith("Append", StringComparison.Ordinal) || next is "Insert" or "Clear" or "Replace" or "ToString")
        || i.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var previous } } }
            && (previous.StartsWith("Append", StringComparison.Ordinal) || previous is "Insert" or "Clear" or "Replace");

    // Stryker.NET's string mutations of calls: a null-or-empty check that lets null, an empty or a blank string through, and Substring
    // or Trim made empty.
    void Strings(InvocationExpressionSyntax i, string called, string receiver, int count, bool bound)
    {
        if (receiver is "string" or "String" && count == 1 && i.ArgumentList.Arguments[0] is { NameColon: null, Expression: var value })
        {
            if (called == "IsNullOrEmpty")
            {
                Add(i, "string", i.Span, $"({Wrap(value)} is null)", $"`{Brief(i)}` lets an empty string through");
                Add(i, "string", i.Span, $"({Wrap(value)} == \"\")", $"`{Brief(i)}` lets null through");
            }
            else if (called == "IsNullOrWhiteSpace" && i.Expression is MemberAccessExpressionSyntax check)
                Add(i, "string", check.Name.Span, "IsNullOrEmpty", $"`{Brief(i)}` lets a blank string through");
        }
        else if (called is "Substring" or "Trim" && receiver.Length > 0 && !bound)
            Add(i, "string", i.Span, "\"\"", $"`{Brief(i)}` becomes `\"\"`");
    }

    // Two arguments in a row that are both names, such as start and end, swapped. Arguments of different types won't compile.
    void SwapArguments(SyntaxNode call, BaseArgumentListSyntax list, string called)
    {
        // ReferenceEquals and a two-argument Equals don't depend on the order.
        if (called is "ReferenceEquals" or "Equals" && list.Arguments.Count == 2) return;
        var arguments = list.Arguments;
        for (var k = 0; k + 1 < arguments.Count; k++)
        {
            var (a, b) = (arguments[k], arguments[k + 1]);
            if (IsName(a.Expression) && IsName(b.Expression) && a.NameColon is null && b.NameColon is null && a.RefKindKeyword.IsKind(SyntaxKind.None) && b.RefKindKeyword.IsKind(SyntaxKind.None) && a.ToString() != b.ToString())
                Add(call, "swap", TextSpan.FromBounds(a.SpanStart, b.Span.End), $"{b}, {a}", $"`{called}` gets `{Brief(a)}` and `{Brief(b)}` the other way round");
        }
    }

    // A value not passed on. One passed by its own name, such as comparer: comparer, is left out, so the parameter's default is used;
    // named arguments of other kinds are as often required parameters named to be read, which don't compile without them, and a true or
    // false one is already flipped. One passed by position that the code was given, such as a serialiser it was built with, becomes default.
    void PassedOn(SyntaxNode call, BaseArgumentListSyntax list)
    {
        if (call is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } }) return;
        for (var k = 0; k < list.Arguments.Count; k++)
        {
            var argument = list.Arguments[k];
            if (argument is { NameColon.Name.Identifier.ValueText: var name, Expression: IdentifierNameSyntax { Identifier.ValueText: var passed } forwarded } && passed == name && !IsMessage(argument) && !Plumbing(name, Declared(forwarded).Type))
                Add(argument, "argument", Removal(list.Arguments, k), "", $"`{Brief(call)}` doesn't pass on `{name}`, so it gets the default");
            else if (argument is { NameColon: null, RefKindKeyword.RawKind: (int)SyntaxKind.None, Expression: IdentifierNameSyntax value } && !IsMessage(argument) && Given(value) is { } type)
                Add(argument, "argument", argument.Span, $"default({type})", $"`{Brief(call)}` gets `default` for `{value}`, so the value it was given isn't passed on");
        }
    }

    // Cancellation tokens and loggers passed on are the don't-write list's plumbing, told by name or by declared type, such as _log or _lifetime.
    static bool Plumbing(string name, TypeSyntax? type) =>
        name == "ct" || name.EndsWith("token", StringComparison.OrdinalIgnoreCase) || name.Contains("logger", StringComparison.OrdinalIgnoreCase)
        || type?.ToString() is { } text && (text.Contains("Logger", StringComparison.Ordinal) || text.Contains("CancellationToken", StringComparison.Ordinal));

    // The declared type of a value the code was given, such as a serialiser it was built with. A default of that type keeps the call's overload.
    static TypeSyntax? Given(ExpressionSyntax e) =>
        e is IdentifierNameSyntax name && Declared(name) is { Given: true, Type: { } type } && !Plumbing(name.Identifier.ValueText, type) ? type : null;

    // A name's declared type, when it's a parameter of the member, local function or lambda it's in, or of its type's primary constructor, or
    // a field, and whether the code was given it: a parameter with a default or a nullable type, or one of the type's.
    static (TypeSyntax? Type, bool Given) Declared(IdentifierNameSyntax e)
    {
        var name = e.Identifier.ValueText;
        foreach (var ancestor in e.Ancestors())
        {
            if (ancestor is SimpleLambdaExpressionSyntax { Parameter.Identifier.ValueText: var single } && single == name) return (null, false);
            if (ancestor is TypeDeclarationSyntax type)
                return (type.ParameterList?.Parameters.FirstOrDefault(p => p.Identifier.ValueText == name)?.Type
                    ?? type.Members.OfType<FieldDeclarationSyntax>().FirstOrDefault(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == name))?.Declaration.Type, true);
            var parameters = ancestor switch
            {
                BaseMethodDeclarationSyntax m => m.ParameterList.Parameters,
                LocalFunctionStatementSyntax f => f.ParameterList.Parameters,
                ParenthesizedLambdaExpressionSyntax l => l.ParameterList.Parameters,
                _ => default,
            };
            if (parameters.FirstOrDefault(p => p.Identifier.ValueText == name) is { } parameter)
                return (parameter.Type, parameter.Default is not null || parameter.Type is NullableTypeSyntax);
        }
        return (null, false);
    }

    // Where a value such as prefix?.Append(suffix) or options.Timeout comes from.
    static IdentifierNameSyntax? Origin(ExpressionSyntax e) => e switch
    {
        IdentifierNameSyntax name => name,
        ConditionalAccessExpressionSyntax c => Origin(c.Expression),
        MemberAccessExpressionSyntax m => Origin(m.Expression),
        _ => null,
    };

    void MemberAccess(MemberAccessExpressionSyntax m)
    {
        if (m.Parent is InvocationExpressionSyntax call && call.Expression == m) return;
        var property = m.Name.Identifier.ValueText;
        if (_propertySiblings.TryGetValue(property, out var sibling)) Add(m, "sibling", m.Name.Identifier.Span, sibling, $"`{Brief(m)}` becomes `{sibling}`");
        if (property == "Date") Add(m, "date", m.Span, m.Expression.ToString(), $"drops `.Date` from `{Brief(m)}`");
        if (property == "Empty" && m.Expression is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.StringKeyword } or IdentifierNameSyntax { Identifier.ValueText: "String" } && !IsMessage(m))
            Add(m, "string", m.Span, "\"mutant\"", "`string.Empty` becomes `\"mutant\"`");
    }

    void Unary(PrefixUnaryExpressionSyntax u)
    {
        switch (u.Kind())
        {
            // A whole condition or an operand of && or || loses its ! when it's forced or dropped.
            case SyntaxKind.LogicalNotExpression when !Forced(u) && !Declares(u):
                Add(u, "negation", u.Span, u.Operand.ToString(), $"`{Brief(u)}` loses its `!`");
                break;
            case SyntaxKind.UnaryMinusExpression when u.Operand is not LiteralExpressionSyntax { Token.Value: 0 or 0L or 0.0 or 0f or 0m }:
                Add(u, "unary", u.OperatorToken.Span, Apart(u.OperatorToken.Span, "+"), $"`{Brief(u)}` uses `+`");
                break;
            case SyntaxKind.UnaryPlusExpression: Add(u, "unary", u.OperatorToken.Span, Apart(u.OperatorToken.Span, "-"), $"`{Brief(u)}` uses `-`"); break;
            case SyntaxKind.BitwiseNotExpression: Add(u, "unary", u.Span, u.Operand.ToString(), $"`{Brief(u)}` loses its `~`"); break;
            case SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression: Update(u, u.OperatorToken); break;
        }
    }

    // ++ for -- and back, though not in a loop's header, where the swap makes the loop endless or crash at once, which says nothing about
    // the tests.
    void Update(ExpressionSyntax node, SyntaxToken token)
    {
        if (InLoopHeader(node)) return;
        var other = token.IsKind(SyntaxKind.PlusPlusToken) ? "--" : "++";
        Add(node, "update", token.Span, Apart(token.Span, other), $"`{Brief(node)}` uses `{other}`");
    }

    // A + or - next to another would join it, as a+-b would become a--b, so it's set apart with a space.
    string Apart(TextSpan span, string sign) =>
        (sign[0] is '+' or '-' && span.Start > 0 && _text[span.Start - 1] is '+' or '-' ? " " : "") + sign
        + (sign[^1] is '+' or '-' && span.End < _text.Length && _text[span.End] is '+' or '-' ? " " : "");

    static bool InLoopHeader(SyntaxNode node) => node.Ancestors().Any(a => a switch
    {
        ForStatementSyntax f => f.Incrementors.Any(i => i.Span.Contains(node.Span)) || f.Condition?.Span.Contains(node.Span) == true,
        WhileStatementSyntax w => w.Condition.Span.Contains(node.Span),
        DoStatementSyntax d => d.Condition.Span.Contains(node.Span),
        _ => false,
    });

    // Stryker.NET's compound assignment swaps. A += that joins text has no -=.
    void Assignment(AssignmentExpressionSyntax a)
    {
        if (a.IsKind(SyntaxKind.CoalesceAssignmentExpression) && a.Parent is ExpressionStatementSyntax && Given(a.Left) is not null)
        {
            Add(a, "argument", a.OperatorToken.Span, "=", $"`{Brief(a)}` always takes the default, so the `{a.Left}` it was given is ignored");
            return;
        }
        string[] others = a.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression when !IsText(a.Right) => ["-="],
            SyntaxKind.SubtractAssignmentExpression => ["+="],
            SyntaxKind.MultiplyAssignmentExpression => ["/="],
            SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression => ["*="],
            SyntaxKind.LeftShiftAssignmentExpression => [">>="],
            SyntaxKind.RightShiftAssignmentExpression => ["<<="],
            SyntaxKind.AndAssignmentExpression => ["|=", "^="],
            SyntaxKind.OrAssignmentExpression => ["&=", "^="],
            SyntaxKind.ExclusiveOrAssignmentExpression => ["|=", "&="],
            // ??= on a nullable value has the value's type and = the nullable's, so the swap is made only where the result isn't used.
            SyntaxKind.CoalesceAssignmentExpression when a.Parent is ExpressionStatementSyntax => ["="],
            _ => [],
        };
        foreach (var other in others) Add(a, "assignment", a.OperatorToken.Span, other, $"`{Brief(a)}` uses `{other}`");
    }

    // is for is not and back, unless it's a whole condition or an operand of && or ||, or declares a variable, which the swap would leave
    // unassigned.
    void IsPattern(IsPatternExpressionSyntax p)
    {
        if (Forced(p) || Declares(p.Pattern)) return;
        if (p.Pattern is UnaryPatternSyntax { Pattern: var inner })
            Add(p, "equality", p.Pattern.Span, inner.ToString(), $"`{Brief(p)}` loses its `not`");
        else
            Add(p, "equality", p.Pattern.Span, p.Pattern is BinaryPatternSyntax ? $"not ({p.Pattern})" : $"not {p.Pattern}", $"`{Brief(p)}` uses `is not`");
    }

    // A string emptied, or an empty one given text, as Stryker.NET does, unless it's a message.
    void StringLiteral(LiteralExpressionSyntax l)
    {
        if (IsMessage(l)) return;
        var suffix = l.IsKind(SyntaxKind.Utf8StringLiteralExpression) ? "u8" : "";
        if (l.Token.ValueText.Length == 0) Add(l, "string", l.Span, $"\"mutant\"{suffix}", $"`{Brief(l)}` becomes `\"mutant\"`");
        else Add(l, "string", l.Span, $"\"\"{suffix}", $"`{Brief(l)}` becomes `\"\"`");
    }

    // A string in a throw, an exception's constructor or a Throw helper's arguments is a message, which mutants.md's don't-write list
    // leaves out.
    static bool IsMessage(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is ThrowStatementSyntax or ThrowExpressionSyntax
                || ancestor is ObjectCreationExpressionSyntax { Type: var type } && type.ToString().EndsWith("Exception", StringComparison.Ordinal)
                || ancestor is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var called } } && called.StartsWith("Throw", StringComparison.Ordinal))
                return true;
            if (ancestor is StatementSyntax or MemberDeclarationSyntax) return false;
        }
        return false;
    }

    // Text joined by +, where - doesn't compile: a string, an interpolation or a call that makes text on either side, at any depth of the
    // chain. A string variable can't be told from its name, so some + of two stay listed and don't compile.
    static bool IsText(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l => l.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        ParenthesizedExpressionSyntax p => IsText(p.Expression),
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) => IsText(b.Left) || IsText(b.Right),
        ObjectCreationExpressionSyntax { Type: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.StringKeyword } } => true,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToString" or "Substring" or "Trim" or "TrimStart" or "TrimEnd" or "PadLeft" or "PadRight" or "Replace" or "ToLower" or "ToUpper" or "ToLowerInvariant" or "ToUpperInvariant" or "Join" or "Concat" or "Format" or "Combine" or "GetFileName" or "GetFileNameWithoutExtension" or "GetDirectoryName" or "GetExtension" or "GetFullPath" or "GetRelativePath" } } => true,
        InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } } => true,
        _ => false,
    };

    // Operands that show a ^ is on integers: a number, arithmetic, bit operations or a hash code, which ?. or ?? may wrap.
    static bool IsInteger(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l => l.IsKind(SyntaxKind.NumericLiteralExpression),
        ParenthesizedExpressionSyntax p => IsInteger(p.Expression),
        CastExpressionSyntax or CheckedExpressionSyntax => true,
        PrefixUnaryExpressionSyntax u => u.IsKind(SyntaxKind.BitwiseNotExpression) || u.IsKind(SyntaxKind.UnaryMinusExpression),
        BinaryExpressionSyntax b => b.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or SyntaxKind.ModuloExpression
            or SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or SyntaxKind.UnsignedRightShiftExpression or SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression
            || b.IsKind(SyntaxKind.CoalesceExpression) && IsInteger(b.Right),
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetHashCode" } } => true,
        ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "GetHashCode" } } } => true,
        _ => false,
    };

    // Operands that show a ^ is on bools: a comparison, a logical operator, a pattern or true or false.
    static bool IsBool(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l => l.IsKind(SyntaxKind.TrueLiteralExpression) || l.IsKind(SyntaxKind.FalseLiteralExpression),
        ParenthesizedExpressionSyntax p => IsBool(p.Expression),
        IsPatternExpressionSyntax => true,
        PrefixUnaryExpressionSyntax u => u.IsKind(SyntaxKind.LogicalNotExpression),
        BinaryExpressionSyntax b => b.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression,
        _ => false,
    };

    // A fallback that can't be null, so ?? can't put it first: a literal other than a string, or a struct's constant such as MinValue.
    static bool IsValue(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l => !l.IsKind(SyntaxKind.StringLiteralExpression),
        PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax } => true,
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "MinValue" or "MaxValue" or "Zero" or "One" or "Epsilon" or "NaN" or "PositiveInfinity" or "NegativeInfinity" or "UnixEpoch" } => true,
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Empty", Expression: var type } => type is not (PredefinedTypeSyntax or IdentifierNameSyntax { Identifier.ValueText: "String" }),
        _ => false,
    };

    // The type a target-typed expression is given where it's declared or returned, or "" where that's not in sight.
    static string DeclaredType(ExpressionSyntax e) => (e.Parent switch
    {
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Type: var type } } } => type,
        EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax { Type: var type } } => type,
        ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax { Type: var type } } => type,
        ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax { ReturnType: var type } } => type,
        ReturnStatementSyntax => e.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.ReturnType,
        _ => null,
    })?.ToString() ?? "";

    // break, continue, yield and goto skipped, as Stryker.NET's removals are. One that's the whole of an if with no else isn't, since
    // forcing the condition false does the same, nor a switch section's break or goto case, or any jump that ends a section, which C#
    // needs, nor an iterator's only yield.
    void Jump(StatementSyntax s)
    {
        if (s.Parent is IfStatementSyntax { Else: null } or BlockSyntax { Statements.Count: 1, Parent: IfStatementSyntax { Else: null } } || EndsSection(s)) return;
        if (s is BreakStatementSyntax && s.Ancestors().FirstOrDefault(a => a is SwitchSectionSyntax or CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax) is SwitchSectionSyntax) return;
        if (s is GotoStatementSyntax && !s.IsKind(SyntaxKind.GotoStatement)) return;
        if (s is YieldStatementSyntax && s.Ancestors().FirstOrDefault(a => a is BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax }) is BlockSyntax body && Yields(body).Count() == 1) return;
        Skip(s, "removal");
    }

    // A jump that ends a switch section, or a block or an if's branch that does, which C# needs, though not a section's only statement
    // before another section: without it, the section's labels join the next one's.
    static bool EndsSection(StatementSyntax jump)
    {
        SyntaxNode node = jump;
        while (node.Parent is BlockSyntax block && block.Statements.Last() == node || node.Parent is IfStatementSyntax or ElseClauseSyntax) node = node.Parent;
        return node.Parent is SwitchSectionSyntax section && section.Statements.Last() == node
            && (node != jump || section.Statements.Count > 1 || section.Parent is SwitchStatementSyntax { Sections: var sections } && sections.Last() == section);
    }

    static IEnumerable<YieldStatementSyntax> Yields(BlockSyntax body) => body.DescendantNodes(n => n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)).OfType<YieldStatementSyntax>();

    // A member's body emptied, as Stryker.NET's block removal does, returning default or yielding nothing, and an => body that returns
    // nothing skipped, as a statement is. One with an out parameter or a ref return can't be, one of a single statement that skipping it
    // already empties needn't be, and Dispose and a body that only logs and the like are left out with the rest of the noise.
    void Body(SyntaxNode body)
    {
        var member = Owner(body).Member;
        if (body is BlockSyntax { Statements.Count: 0 } || member is "Dispose" or "DisposeAsync") return;
        var parent = body.Parent!;
        var (type, parameters, modifiers) = parent switch
        {
            MethodDeclarationSyntax m => (m.ReturnType, m.ParameterList.Parameters, m.Modifiers),
            OperatorDeclarationSyntax o => (o.ReturnType, o.ParameterList.Parameters, o.Modifiers),
            ConversionOperatorDeclarationSyntax c => (c.Type, c.ParameterList.Parameters, c.Modifiers),
            BaseMethodDeclarationSyntax m => (null, m.ParameterList.Parameters, m.Modifiers),
            LocalFunctionStatementSyntax f => (f.ReturnType, f.ParameterList.Parameters, f.Modifiers),
            AccessorDeclarationSyntax { Parent.Parent: BasePropertyDeclarationSyntax property } a => (a.IsKind(SyntaxKind.GetAccessorDeclaration) ? property.Type : null, default, a.Modifiers),
            _ => default,
        };
        if (type is RefTypeSyntax || parameters.Any(p => p.Modifiers.Any(SyntaxKind.OutKeyword))) return;
        var task = modifiers.Any(SyntaxKind.AsyncKeyword) && type is IdentifierNameSyntax { Identifier.ValueText: "Task" or "ValueTask" } or QualifiedNameSyntax { Right.Identifier.ValueText: "Task" or "ValueTask" };
        var nothing = type is null or PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword } || task;
        if (body is not BlockSyntax block)
        {
            if (nothing && body is ArrowExpressionClauseSyntax { Expression: var expression } && Acts(expression))
                Add(expression, "statement", TextSpan.FromBounds(body.SpanStart, parent.Span.End), "{ }", $"skips `{Brief(expression)}`");
            return;
        }
        var iterator = Yields(block).Any();
        if (nothing && !iterator && block.Statements is [ExpressionStatementSyntax] || OnlyNoise(block.Statements)) return;
        var name = parent switch { LocalFunctionStatementSyntax f => f.Identifier.ValueText, AccessorDeclarationSyntax a => $"{member}'s {a.Keyword.Text}", _ => member };
        var (replacement, effect) = iterator ? ("{ yield break; }", "yields nothing") : nothing ? ("{ }", "does nothing") : ("{ return default!; }", "returns default");
        Add(body, "block", body.Span, replacement, $"`{name}` {effect}");
    }

    // [GeneratedRegex] patterns, which the source generator compiles into the library, so a mutant there takes effect.
    void GeneratedRegexes(AttributeListSyntax list)
    {
        foreach (var attribute in list.Attributes)
        {
            var name = attribute.Name.ToString();
            if ((name.EndsWith("GeneratedRegex", StringComparison.Ordinal) || name.EndsWith("GeneratedRegexAttribute", StringComparison.Ordinal))
                && attribute.ArgumentList is { Arguments: [{ NameEquals: null, Expression: LiteralExpressionSyntax pattern }, ..] })
                RegexPattern(attribute, pattern);
        }
    }

    // new Regex(...) with its type written, or target-typed where it's declared or returned as a Regex.
    static bool IsRegex(BaseObjectCreationExpressionSyntax creation) =>
        (creation is ObjectCreationExpressionSyntax { Type: var type } ? type.ToString() : DeclaredType(creation)) is "Regex" or "System.Text.RegularExpressions.Regex";

    // Stryker.NET's regex mutations, in a pattern written as a regular, verbatim or one-line raw string. Each mutated pattern must still
    // parse, and its text goes back into the literal escaped as the literal needs.
    void RegexPattern(SyntaxNode owner, LiteralExpressionSyntax literal)
    {
        if (!Decode(literal.Token, out var pattern, out var offsets) || !Parses(pattern)) return;
        var edits = new List<(int Start, int End, string Text, string What)>();
        RegexEdits(pattern, edits);
        var at = literal.Token.SpanStart;
        foreach (var (start, end, text, what) in edits)
        {
            var mutated = string.Concat(pattern.AsSpan(0, start), text, pattern.AsSpan(end));
            if (mutated == pattern || !Parses(mutated)) continue;
            Add(owner, "regex", TextSpan.FromBounds(at + offsets[start], at + offsets[end]), Escape(literal.Token.Text, text), $"`{Brief(literal)}` {what}");
        }
    }

    // The literal's value, with where each character starts in the token and, last, where the value ends. Escapes other than the simple
    // ones and \u aren't decoded, so a literal with them is left alone, as is one whose decoding doesn't match Roslyn's.
    static bool Decode(SyntaxToken token, out string value, out int[] offsets)
    {
        var text = token.Text;
        var chars = new StringBuilder();
        var from = new List<int>();
        var end = 0;
        var known = true;
        if (text.StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            var quotes = text.Length - text.TrimStart('"').Length;
            known = !text.Contains('\n') && text.Length > 2 * quotes;
            for (var k = quotes; known && k < text.Length - quotes; k++) Take(text[k], k, k + 1);
        }
        else if (text.StartsWith("@\"", StringComparison.Ordinal))
        {
            for (var k = 2; k < text.Length - 1; k++)
                if (text[k] == '"') Take('"', k, ++k + 1);
                else Take(text[k], k, k + 1);
        }
        else if (text.StartsWith('"'))
        {
            for (var k = 1; known && k < text.Length - 1; k++)
            {
                if (text[k] != '\\') Take(text[k], k, k + 1);
                else if (text[k + 1] == 'u' && k + 6 < text.Length && ushort.TryParse(text.AsSpan(k + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                {
                    Take((char)code, k, k + 6);
                    k += 5;
                }
                else if (text[k + 1] switch { '\'' => '\'', '"' => '"', '\\' => '\\', '0' => '\0', 'a' => '\a', 'b' => '\b', 'e' => '\e', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', _ => (char?)null } is { } simple)
                {
                    Take(simple, k, k + 2);
                    k++;
                }
                else known = false;
            }
        }
        else known = false;
        value = chars.ToString();
        from.Add(end);
        offsets = [.. from];
        return known && value.Length > 0 && value == token.ValueText;

        void Take(char c, int start, int after)
        {
            chars.Append(c);
            from.Add(start);
            end = after;
        }
    }

    // Text for the literal: a regular string escapes \ and ", a verbatim one doubles ", and a raw one takes it as it is.
    static string Escape(string literal, string text) =>
        literal.StartsWith("\"\"\"", StringComparison.Ordinal) ? text
        : literal.StartsWith('@') ? text.Replace("\"", "\"\"", StringComparison.Ordinal)
        : text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static bool Parses(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Stryker.NET's regex mutators: anchors and quantifiers dropped, quantifiers made lazy, written as ranges or moved by one, classes
    // negated, made to match anything, emptied of one item or their ranges moved by one, \d \w and \s negated, unescaped or made to match
    // anything, \p negated, lookarounds negated and groups made non-capturing.
    static void RegexEdits(string p, List<(int Start, int End, string Text, string What)> edits)
    {
        for (var i = 0; i < p.Length;)
        {
            var c = p[i];
            if (c == '\\' && i + 1 < p.Length)
            {
                var e = p[i + 1];
                var end = EscapeEnd(p, i);
                if (e is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
                {
                    var other = char.IsUpper(e) ? char.ToLowerInvariant(e) : char.ToUpperInvariant(e);
                    edits.Add((i, end, $"\\{other}", $"uses `\\{other}` for `\\{e}`"));
                    edits.Add((i, end, e.ToString(), $"drops the `\\` of `\\{e}`"));
                    edits.Add((i, end, $"[\\{e}\\{other}]", $"matches any character for `\\{e}`"));
                }
                else if (e is 'p' or 'P' && end > i + 3) edits.Add((i + 1, i + 2, e == 'p' ? "P" : "p", $"negates `{p[i..end]}`"));
                else if (e is 'A' or 'Z' or 'z' or 'b' or 'B' or 'G') edits.Add((i, end, "", $"drops the anchor `\\{e}`"));
                i = end;
            }
            else if (c == '[' && ClassEnd(p, i) is var close and > 0)
            {
                CharClass(p, i, close, edits);
                i = close + 1;
            }
            else if (c is '^' or '$')
            {
                edits.Add((i, i + 1, "", $"drops the anchor `{c}`"));
                i++;
            }
            else if (c == '(')
            {
                var body = GroupStart(p, i);
                var opening = p[i..body];
                if (opening switch { "(?=" => "(?!", "(?!" => "(?=", "(?<=" => "(?<!", "(?<!" => "(?<=", _ => null } is { } negated) edits.Add((i, body, negated, $"uses `{negated}` for `{opening}`"));
                else if (opening == "(") edits.Add((i, body, "(?:", "makes a group non-capturing"));
                i = body;
            }
            else if (Quantifier(p, i) is var after and > 0)
            {
                var lazy = after < p.Length && p[after] == '?';
                var quantifier = p[i..after];
                var whole = lazy ? after + 1 : after;
                edits.Add((i, whole, "", $"drops the quantifier `{p[i..whole]}`"));
                if (!lazy) edits.Add((after, after, "?", $"makes `{quantifier}` lazy"));
                foreach (var range in Ranges(quantifier)) edits.Add((i, after, range, $"uses `{range}` for `{quantifier}`"));
                i = whole;
            }
            else i++;
        }
    }

    // The end of a quantifier starting at i (? * + {n} {n,} {n,m}), or -1.
    static int Quantifier(string p, int i)
    {
        if (p[i] is '*' or '+' or '?') return i + 1;
        if (p[i] != '{') return -1;
        var k = Digits(p, i + 1);
        if (k == i + 1 || k - i > 10) return -1;
        if (k < p.Length && p[k] == ',')
        {
            var start = k + 1;
            k = Digits(p, start);
            if (k - start > 9) return -1;
        }
        return k < p.Length && p[k] == '}' ? k + 1 : -1;
    }

    // Stryker.NET's range edits of a quantifier: ? * and + written as ranges, and a range's bounds moved by one.
    static IEnumerable<string> Ranges(string q)
    {
        switch (q)
        {
            case "?": return ["{1,1}", "{0,0}", "{0,2}"];
            case "*": return ["{1,}"];
            case "+": return ["{0,}", "{2,}"];
        }
        var comma = q.IndexOf(',');
        if (comma < 0) return [];
        var low = int.Parse(q.AsSpan(1, comma - 1), CultureInfo.InvariantCulture);
        if (comma == q.Length - 2) return low > 0 ? [$"{{{low - 1},}}", $"{{{low + 1},}}"] : [$"{{{low + 1},}}"];
        var high = int.Parse(q.AsSpan(comma + 1, q.Length - comma - 2), CultureInfo.InvariantCulture);
        var ranges = new List<string>();
        if (low > 0) ranges.Add($"{{{low - 1},{high}}}");
        if (low + 1 <= high) ranges.Add($"{{{low + 1},{high}}}");
        if (high - 1 >= low) ranges.Add($"{{{low},{high - 1}}}");
        ranges.Add($"{{{low},{high + 1}}}");
        return ranges;
    }

    // The end of the escape at i: \p{..}, \k<..>, \x with two hex digits, \u with four, \cX, a backreference's digits, or one character.
    static int EscapeEnd(string p, int i)
    {
        if (i + 1 >= p.Length) return p.Length;
        int close;
        return p[i + 1] switch
        {
            'p' or 'P' when i + 2 < p.Length && p[i + 2] == '{' && (close = p.IndexOf('}', i + 3)) > 0 => close + 1,
            'k' when i + 2 < p.Length && p[i + 2] is '<' or '\'' && (close = p.IndexOf(p[i + 2] == '<' ? '>' : '\'', i + 3)) > 0 => close + 1,
            'x' => Math.Min(p.Length, i + 4),
            'u' => Math.Min(p.Length, i + 6),
            'c' => Math.Min(p.Length, i + 3),
            >= '1' and <= '9' => Digits(p, i + 1),
            _ => i + 2,
        };
    }

    static int Digits(string p, int k)
    {
        while (k < p.Length && char.IsAsciiDigit(p[k])) k++;
        return k;
    }

    // The ] closing the class that opens at i, or -1: a ] first in the class is literal, and a subtraction's class is nested.
    static int ClassEnd(string p, int i)
    {
        var k = i + 1;
        if (k < p.Length && p[k] == '^') k++;
        if (k < p.Length && p[k] == ']') k++;
        for (; k < p.Length; k++)
        {
            if (p[k] == '\\') k++;
            else if (p[k] == '[' && p[k - 1] == '-')
            {
                k = ClassEnd(p, k);
                if (k < 0) return -1;
            }
            else if (p[k] == ']') return k;
        }
        return -1;
    }

    // A class negated, made to match anything, and, unless it has a subtraction, each item dropped when it has several and each range of
    // two plain characters moved by one at either end.
    static void CharClass(string p, int open, int close, List<(int Start, int End, string Text, string What)> edits)
    {
        var whole = p[open..(close + 1)];
        var negated = p[open + 1] == '^';
        edits.Add(negated ? (open + 1, open + 2, "", $"uses `[` for the `[^` of `{whole}`") : (open + 1, open + 1, "^", $"negates `{whole}`"));
        edits.Add((open, close + 1, "[\\w\\W]", $"matches any character for `{whole}`"));
        if (whole.Contains("-[", StringComparison.Ordinal)) return;
        var items = new List<(int Start, int End)>();
        for (var k = negated ? open + 2 : open + 1; k < close;)
        {
            var start = k;
            k = p[k] == '\\' ? EscapeEnd(p, k) : k + 1;
            if (k + 1 < close && p[k] == '-')
            {
                var dash = k;
                k = p[k + 1] == '\\' ? EscapeEnd(p, k + 1) : k + 2;
                if (dash == start + 1 && k == dash + 2) RangeEnds(p, start, whole, edits);
            }
            items.Add((start, k));
        }
        if (items.Count > 1)
            foreach (var (start, end) in items) edits.Add((start, end, "", $"drops `{p[start..end]}` from `{whole}`"));
    }

    static void RangeEnds(string p, int at, string whole, List<(int Start, int End, string Text, string What)> edits)
    {
        var (low, high) = (p[at], p[at + 2]);
        foreach (var (position, value) in new[] { (at, low - 1), (at, low + 1), (at + 2, high + 1), (at + 2, high - 1) })
        {
            var moved = (char)value;
            if (value is < 0x21 or > 0x7E || moved is '\\' or ']' or '[' or '^' or '-' || (position == at ? value > high : value < low)) continue;
            var range = position == at ? $"{moved}-{high}" : $"{low}-{moved}";
            edits.Add((position, position + 1, moved.ToString(), $"uses `{range}` for `{low}-{high}` in `{whole}`"));
        }
    }

    // Where a group's own pattern starts after its opening, such as ( (?: (?= (?<! (?<name> or (?i:; a comment is skipped whole.
    static int GroupStart(string p, int i)
    {
        if (i + 1 >= p.Length || p[i + 1] != '?') return i + 1;
        if (i + 2 >= p.Length) return p.Length;
        var c = p[i + 2];
        if (c is ':' or '=' or '!' or '>') return i + 3;
        if (c == '<' && i + 3 < p.Length && p[i + 3] is '=' or '!') return i + 4;
        if (c is '#' or '<' or '\'')
        {
            var close = p.IndexOf(c switch { '#' => ')', '<' => '>', _ => '\'' }, i + 3);
            return close < 0 ? p.Length : close + 1;
        }
        var k = i + 2;
        while (k < p.Length && (char.IsAsciiLetter(p[k]) || p[k] == '-')) k++;
        return k < p.Length && p[k] is ':' or ')' ? k + 1 : k;
    }

    // The condition of an if, ?:, when, query where or loop, or a whole bool value, which Force and ForceBool set true and false, or an
    // operand of && or ||, which DropOperands drops: negating it or swapping its comparison would show nothing those don't.
    static bool Forced(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax) node = node.Parent;
        return node is ExpressionSyntax e && IsWholeBool(e) || ConditionOf(node.Parent).Condition == node
            || node.Parent is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression));
    }

    // The condition of an if, ?:, when, query where or loop, and the code that runs only when it's true, where that's in sight.
    static (ExpressionSyntax? Condition, TextSpan? Guards) ConditionOf(SyntaxNode? node) => node switch
    {
        IfStatementSyntax s => (s.Condition, s.Statement.Span),
        ConditionalExpressionSyntax c => (c.Condition, c.WhenTrue.Span),
        WhenClauseSyntax { Parent: SwitchExpressionArmSyntax arm } w => (w.Condition, arm.Expression.Span),
        WhenClauseSyntax { Parent.Parent: SwitchSectionSyntax { Statements.Count: > 0 } section } w => (w.Condition, section.Statements.Span),
        WhenClauseSyntax w => (w.Condition, null),
        WhereClauseSyntax w => (w.Condition, null),
        WhileStatementSyntax w => (w.Condition, w.Statement.Span),
        DoStatementSyntax d => (d.Condition, null),
        ForStatementSyntax f => (f.Condition, f.Statement.Span),
        _ => (null, null),
    };

    // The whole expression a node is in, up to its statement or clause or the body of a lambda it's in, and out of any interpolated string.
    static SyntaxNode Root(SyntaxNode node)
    {
        while (node.Parent is ExpressionSyntax and not AnonymousFunctionExpressionSyntax or ArgumentSyntax or BaseArgumentListSyntax or InterpolationSyntax or InterpolationAlignmentClauseSyntax)
            node = node.Parent;
        return node;
    }

    // Whether the expression or statement a node is in is free of logging, tracing, metrics, sleeps, cancellation checks, assertions and
    // disposal. A lambda in it is judged apart, so a retry's onRetry that logs doesn't hide the call, though one passed to a log call is noise.
    bool Quiet(SyntaxNode node)
    {
        var root = Root(node);
        if (root.Parent is AnonymousFunctionExpressionSyntax lambda && !Quiet(lambda)) return false;
        ref var quiet = ref CollectionsMarshal.GetValueRefOrAddDefault(_quiet, root.Span, out var known);
        if (!known) quiet = !Noisy(root);
        return quiet;
    }

    // Noise in an expression's text, leaving out the lambdas in it.
    bool Noisy(SyntaxNode root)
    {
        var from = root.SpanStart;
        foreach (var lambda in root.DescendantNodes(n => n == root || n is not AnonymousFunctionExpressionSyntax).OfType<AnonymousFunctionExpressionSyntax>())
        {
            if (Noise.IsMatch(Text(TextSpan.FromBounds(from, lambda.SpanStart)))) return true;
            from = lambda.Span.End;
        }
        return Noise.IsMatch(Text(TextSpan.FromBounds(from, root.Span.End)));
    }

    // Statements that all log, trace, sleep and the like, apart from a break, continue or return that ends them.
    bool OnlyNoise(SyntaxList<StatementSyntax> statements)
    {
        var count = statements.LastOrDefault() is BreakStatementSyntax or ContinueStatementSyntax or ReturnStatementSyntax { Expression: null } ? statements.Count - 1 : statements.Count;
        return count > 0 && statements.Take(count).All(s => s is ExpressionStatementSyntax && !Quiet(s));
    }

    void Add(SyntaxNode node, string kind, TextSpan span, string replacement, string description, bool rest = false)
    {
        if (node is ExpressionSyntax or ArgumentSyntax or ExpressionStatementSyntax && !Quiet(node)) return;
        if (Text(span).SequenceEqual(replacement) || !_seen.Add((span.Start, span.End, replacement))) return;
        var edit = Unique(span.Start, span.End, replacement);
        // A file that isn't UTF-8 needs an ASCII find and replace, as mutants.cs reads it byte for byte.
        if (_latin1 && !Ascii.IsValid(edit.Find + edit.Replace)) return;
        var (type, fullType, member) = Owner(node);
        var line = _lines.GetLinePosition(span.Start).Line + 1;
        var last = _lines.GetLinePosition(Math.Max(span.Start, span.End - 1)).Line + 1;
        _found.Add(new Candidate(file, type, fullType, member, kind, line, edit.Find, edit.Replace, description, rest || _restKinds.Contains(kind), hits is not null && NotRun(hits, line, last)));
    }

    // The lines of the edit that the coverage has, none of them run. A line it doesn't have, such as a declaration or a brace, doesn't
    // count, so an edit only on those isn't marked.
    static bool NotRun(Dictionary<int, int> hits, int first, int last)
    {
        var seen = false;
        for (var n = first; n <= last; n++)
        {
            if (!hits.TryGetValue(n, out var count)) continue;
            if (count > 0) return false;
            seen = true;
        }
        return seen;
    }

    // The edit's own text when it occurs once in the file, else its lines, else a line more above and below in turn until it does, as
    // the whole file does.
    (string Find, string Replace) Unique(int start, int end, string replacement)
    {
        var (from, to) = (start, end);
        for (var step = 0; ; step++)
        {
            if (step == 1) (from, to) = (LineStart(start), LineEnd(end));
            else if (step > 1 && step % 2 == 0) from = LineStart(Math.Max(0, from - 1));
            else if (step > 1) to = LineEnd(Math.Min(_text.Length, to + 1));
            var find = _text[from..to];
            if (!string.IsNullOrWhiteSpace(find) && Occurrences(find) == 1) return (find, string.Concat(_text.AsSpan(from, start - from), replacement, _text.AsSpan(end, to - end)));
        }
    }

    int LineStart(int position) => position == 0 ? 0 : _text.LastIndexOf('\n', position - 1) + 1;

    int LineEnd(int position) => _text.IndexOf('\n', position) is >= 0 and var at ? at : _text.Length;

    // Overlapping occurrences count, as they do in mutants.cs.
    int Occurrences(string find)
    {
        var count = 0;
        for (var at = _text.IndexOf(find, StringComparison.Ordinal); at >= 0 && count < 2; at = _text.IndexOf(find, at + 1, StringComparison.Ordinal)) count++;
        return count;
    }

    // A member named as uncovered.cs prints it: alone, or after enough of its type's trailing names to tell it apart, such as
    // Range.Generate or GenInt.Range.Generate. A nested type may be written after a / or +, as coverlet and reflection write it.
    public static bool Matches(string name, string fullType, string member)
    {
        name = name.Replace('/', '.').Replace('+', '.');
        var dot = name.LastIndexOf('.');
        if (!name.AsSpan(dot + 1).Equals(member, StringComparison.OrdinalIgnoreCase)) return false;
        return dot < 0 || IsTypeNamed(fullType, name.AsSpan(0, dot));
    }

    // A type's full name names it, and so do its whole trailing names, as GenInt.Range does CsCheck.GenInt.Range.
    public static bool IsTypeNamed(string fullType, ReadOnlySpan<char> name) =>
        fullType.AsSpan().EndsWith(name, StringComparison.OrdinalIgnoreCase) && (fullType.Length == name.Length || fullType[^(name.Length + 1)] == '.');

    // The member uncovered.cs names: field and property initializers, and a primary constructor's base arguments and defaults, run in the
    // constructor, and lambdas and local functions in their member. Its type is the innermost one, and the full one has its namespaces and
    // outer types too.
    static (string Type, string FullType, string Member) Owner(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            var member = ancestor switch
            {
                MethodDeclarationSyntax m => m.Identifier.ValueText,
                ConstructorDeclarationSyntax => "ctor",
                DestructorDeclarationSyntax => "Finalize",
                PropertyDeclarationSyntax p => p.Initializer is { } initializer && initializer.Span.Contains(node.Span) ? "ctor" : p.Identifier.ValueText,
                IndexerDeclarationSyntax => "this",
                EventDeclarationSyntax e => e.Identifier.ValueText,
                OperatorDeclarationSyntax o => OperatorName(o),
                ConversionOperatorDeclarationSyntax c => c.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword) ? "op_Implicit" : c.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword) ? "op_CheckedExplicit" : "op_Explicit",
                FieldDeclarationSyntax or EventFieldDeclarationSyntax or PrimaryConstructorBaseTypeSyntax or ParameterListSyntax { Parent: TypeDeclarationSyntax } => "ctor",
                GlobalStatementSyntax => "Main",
                _ => null,
            };
            if (member is not null) return (ancestor.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>(t => t.Identifier.ValueText.Length > 0)?.Identifier.ValueText ?? "", FullType(ancestor), member);
        }
        return ("", "", "");
    }

    // As the coverage names a type, outermost first, with top-level statements in Program.
    static string FullType(SyntaxNode node)
    {
        var name = string.Join('.', node.AncestorsAndSelf().Select(a => a switch
        {
            BaseTypeDeclarationSyntax { Identifier.ValueText: { Length: > 0 } type } => type,
            BaseNamespaceDeclarationSyntax n => n.Name.ToString(),
            _ => null,
        }).OfType<string>().Reverse());
        return name.Length > 0 ? name : "Program";
    }

    // + and - with one operand are UnaryPlus and UnaryNegation, and an instance ++ or --, with none, is IncrementAssignment or DecrementAssignment.
    static string OperatorName(OperatorDeclarationSyntax o)
    {
        var text = o.OperatorToken.Text;
        var name = (text, o.ParameterList.Parameters.Count) switch
        {
            ("+", 1) => "UnaryPlus",
            ("-", 1) => "UnaryNegation",
            ("++" or "--", 0) => _operators[text] + "Assignment",
            _ => _operators.GetValueOrDefault(text, text),
        };
        return o.CheckedKeyword.IsKind(SyntaxKind.CheckedKeyword) ? "op_Checked" + name : "op_" + name;
    }

    // What mutants.cs counts as a member's body.
    static bool InBody(SyntaxNode node) => node.Ancestors().Any(a => a is BlockSyntax or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or GlobalStatementSyntax);

    // Attributes and the interpolated strings of messages aren't searched, though Find still visits the node itself. C# compiles a public or
    // internal const, an enum member's value and a default parameter value into each caller, so those aren't either, nor a delegate's
    // defaults, which are in no member.
    static bool SearchInside(SyntaxNode node) => node is not (AttributeListSyntax or EnumMemberDeclarationSyntax)
        && (node is not InterpolatedStringExpressionSyntax || !IsMessage(node))
        && (node is not ParameterSyntax { Default: not null, Parent.Parent: var declaration } || declaration is not DelegateDeclarationSyntax && IsPrivate(declaration))
        && (node is not FieldDeclarationSyntax field || !field.Modifiers.Any(SyntaxKind.ConstKeyword) || IsPrivate(field));

    static bool IsPrivate(SyntaxNode? declaration) => declaration is MemberDeclarationSyntax member
        && (member.Modifiers.Any(SyntaxKind.PrivateKeyword) && !member.Modifiers.Any(SyntaxKind.ProtectedKeyword)
            || !member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))
                && member.Parent is TypeDeclarationSyntax and not InterfaceDeclarationSyntax);

    static bool Declares(SyntaxNode node) => node.DescendantNodesAndSelf().Any(d => d is SingleVariableDesignationSyntax or DeclarationExpressionSyntax);

    static bool IsName(ExpressionSyntax expression) => expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax or ThisExpressionSyntax or MemberAccessExpressionSyntax };

    // An out argument to a variable that's already there, such as an out parameter, which forcing the expression would leave unassigned.
    static bool AssignsOut(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<ArgumentSyntax>().Any(a => a.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) && a.Expression is not DeclarationExpressionSyntax);

    // An operand moved to the other side keeps its grouping.
    static string Wrap(ExpressionSyntax expression) => expression is IdentifierNameSyntax or LiteralExpressionSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or ThisExpressionSyntax
        ? expression.ToString()
        : $"({expression})";

    // An item and the comma after it, or before it when it's last, so the list stays well formed.
    static TextSpan Removal<T>(SeparatedSyntaxList<T> list, int k) where T : SyntaxNode =>
        k < list.SeparatorCount ? TextSpan.FromBounds(list[k].SpanStart, list.GetSeparator(k).Span.End)
        : k > 0 ? TextSpan.FromBounds(list.GetSeparator(k - 1).SpanStart, list[k].Span.End)
        : list[k].Span;

    // A node's text is the file's under its span, as the tree is parsed from _text, so it needn't be built with ToString.
    ReadOnlySpan<char> Text(TextSpan span) => _text.AsSpan(span.Start, span.Length);

    string Brief(SyntaxNode node) => Brief(node.Span);

    // The text on one line, cut to 47 characters and "..." past 50. It's read from the file, not built with ToString, so a long node isn't copied whole; a space and a character can take 50 to 52.
    string Brief(TextSpan span)
    {
        Span<char> brief = stackalloc char[52];
        var length = 0;
        var space = false;
        foreach (var c in Text(span))
        {
            if (char.IsWhiteSpace(c)) { space = length > 0; continue; }
            if (space) brief[length++] = ' ';
            brief[length++] = c;
            space = false;
            if (length > 50) break;
        }
        return length <= 50 ? new string(brief[..length]) : string.Concat(brief[..47], "...");
    }

    // As mutants.cs reads it: a file that isn't UTF-8 is read byte for byte as Latin-1.
    static (string Text, Encoding Encoding) ReadSource(string file)
    {
        using var reader = new StreamReader(file, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        try { return (reader.ReadToEnd(), reader.CurrentEncoding); }
        catch (DecoderFallbackException) { return (Encoding.Latin1.GetString(File.ReadAllBytes(file)), Encoding.Latin1); }
    }
}
