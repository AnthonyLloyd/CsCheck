#!/usr/bin/env dotnet
#:package Microsoft.CodeAnalysis.CSharp@5.0.0
#:property PublishAot=false
#:property Optimize=true
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property RunAnalyzers=false
#:property LangVersion=latest

// Runs hand-written mutants, find/replace edits listed in a JSON file, against a test project and reports which ones its tests detect.
// No file in the repo is edited: MSBuild hands over each mutated library's csc arguments without compiling (the design-time build
// flags), each mutant compiles from a temp copy of its file through the compiler server, and its DLL goes into a copy of the test
// output. VSTest and Microsoft.Testing.Platform test projects, on Windows, Linux and macOS. The JSON's paths are relative to the
// repo root: run it from there, or from anywhere with --root, such as when the repo's global.json pins SDK 8 or 9.
// The #:property lines stop a repo's Directory.Build.props from changing how this builds, when the skill is copied into a repo.
// dotnet run --file mutants.cs -- <mutants.json> [--root <repo>] [--only <id,id>] [--parallel 4] [--timeout <seconds>] [--filter-only]

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

const string Usage = "usage: dotnet run --file mutants.cs -- <mutants.json> [--root <repo>] [--only <id,id>] [--parallel 4] [--timeout <seconds>] [--filter-only]";
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}
var parallel = 4;
int? timeout = null;
var filterOnly = false;
var root = Environment.CurrentDirectory;
string[]? only = null;
for (var i = 1; i < args.Length; i++)
{
    if (args[i] == "--filter-only")
    {
        filterOnly = true;
        continue;
    }
    var (option, value) = (args[i], ++i < args.Length ? args[i] : "");
    if (option == "--parallel" && int.TryParse(value, out var p) && p is >= 1 and <= 16) parallel = p;
    else if (option == "--timeout" && int.TryParse(value, out var t) && t > 0) timeout = Math.Min(t, int.MaxValue / 1000);
    else if (option == "--root" && value.Length > 0) root = Path.GetFullPath(value);
    else if (option == "--only" && MutantRun.SplitNames(value) is { Length: > 0 } ids) only = ids;
    else
    {
        Console.Error.WriteLine(option switch
        {
            "--parallel" => "error: --parallel takes 1 to 16",
            "--timeout" => "error: --timeout takes a number of seconds",
            "--root" or "--only" => $"error: {option} takes a value",
            _ => $"error: there's no option {option}",
        });
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"error: there's no folder {root}");
    return 1;
}
try
{
    return await new MutantRun(Path.GetFullPath(args[0]), Path.TrimEndingDirectorySeparator(root), only, parallel, timeout, filterOnly).RunAsync();
}
// Parallel.ForEach wraps what a copy throws.
catch (Exception e) when ((e is AggregateException aggregate ? aggregate.Flatten().InnerException : e) is (RunException or IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException) and var cause)
{
    Console.Error.WriteLine($"error: {cause.Message}");
    return 1;
}

sealed class RunException(string message) : Exception(message);

sealed record Spec(string TestProject, MutantEntry[] Mutants, string? Framework = null, string? Filter = null, string? RerunFilter = null, string[]? Exclude = null, string? Candidates = null);

// A mutant as the spec gives it: written out, or by its id in the candidates file, with a bug or an expect of its own if needed.
sealed record MutantEntry(string Id, string? Bug = null, string? File = null, string? Find = null, string? Replace = null, string? Expect = null);

sealed record CandidateFile(MutantEntry[] Mutants);

sealed record MutantSpec(string Id, string Bug, string File, string Find, string Replace, string? Expect = null);

sealed record ToolRun(int? ExitCode, string StandardOutput, string Text, string Log, double Seconds);

sealed record TestOutcome(string Status, string[] Failed, int Total, double Seconds, string Log);

sealed record Library(string Project, string Directory, string Compiler, string AssemblyFileName, string[] Arguments, CSharpParseOptions ParseOptions, Dictionary<string, int> SourceIndexes);

// A mutant's change to its file's Text: Replacement in place of the Length characters at Index.
sealed record Edit(MutantSpec Spec, string File, string Project, string Text, string Replacement, int Index, int Length, Encoding Encoding);

sealed record Mutant(MutantSpec Spec, Library Library, int SourceIndex, string Source, string? Warning);

sealed record Result(string Status, string Detail, string[] FailedTests, double Seconds);

sealed record ReportEntry(string Id, string Status, string Detail, string Bug, string? Expect, string? Warning, string[] FailedTests, double Seconds);

sealed partial class MutantRun(string specPath, string root, string[]? only, int parallel, int? timeoutOption, bool filterOnly)
{
    static readonly JsonSerializerOptions _specOptions = new(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };
    static readonly JsonSerializerOptions _reportOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly XNamespace _trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    // macOS file systems ignore case by default, as Windows does.
    static readonly StringComparer _pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    // The dotnet that runs this script, so its children start even where dotnet isn't on the PATH.
    static readonly string _dotnet = DotnetHost();
    readonly ConcurrentDictionary<string, Result> _results = new();
    readonly Dictionary<string, string> _originals = new(_pathComparer);
    // Each file is read once, and its mutants share the text.
    readonly Dictionary<string, ((string Text, Encoding Encoding) Source, string Project)> _files = new(_pathComparer);
    readonly string _root = root;
    string _out = "";
    string[] _exclude = [];
    string[] _slots = [];
    string _testAssembly = "";
    string? _runSettings;
    bool _mtp;
    bool _treeNodeFilter;
    double _compileSeconds;

    public async Task<int> RunAsync()
    {
        var wall = Environment.TickCount64;
        var spec = ReadJson<Spec>(specPath);
        if (spec.Mutants.Length == 0) throw new RunException($"{specPath} has no mutants");
        if (Array.FindIndex(spec.Mutants, m => m is null) is >= 0 and var empty) throw new RunException($"{specPath}: mutant {empty + 1} is null");
        if (Array.FindIndex(spec.Exclude ?? [], e => e is null) is >= 0 and var nullTest) throw new RunException($"{specPath}: test {nullTest + 1} in exclude is null");
        var all = Resolve(spec);
        // Ids name folders, which ignore case on Windows and macOS.
        var duplicates = all.CountBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Where(c => c.Value > 1).Select(c => c.Key).ToArray();
        if (duplicates.Length > 0) throw new RunException($"duplicate mutant ids, ignoring case: {string.Join(", ", duplicates)}");
        var selected = all;
        if (only is not null)
        {
            var unknown = Array.FindAll(only, id => !Array.Exists(all, m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
            if (unknown.Length > 0) throw new RunException($"--only names mutants that aren't in {Path.GetFileName(specPath)}: {string.Join(", ", unknown)}");
            selected = Array.FindAll(all, m => only.Contains(m.Id, StringComparer.OrdinalIgnoreCase));
        }
        _exclude = spec.Exclude ?? [];
        var filter = spec.Filter;

        // The spec is checked before the last run's results are deleted, so a typo doesn't cost them.
        var testProject = ResolveProject(Path.GetFullPath(Path.Combine(_root, Portable(spec.TestProject))));
        var edits = Array.ConvertAll(selected, m => Prepare(m, testProject));
        _out = Path.Combine(Path.GetDirectoryName(specPath)!, Path.GetFileNameWithoutExtension(specPath) + "-run");
        using var specLock = OpenLock(_out + ".lock", $"another run of {Path.GetFileName(specPath)} is going");
        // An --only run keeps the earlier results of the spec's other mutants, and their logs, so the sweep's counts outlive a rerun.
        var earlier = only is null ? null : EarlierResults(Path.Combine(_out, "results.json"), all);
        if (earlier is not null) DeleteRunExcept(earlier.Select(e => e.Id).Except(Array.ConvertAll(selected, m => m.Id), StringComparer.OrdinalIgnoreCase));
        else if (Directory.Exists(_out)) Directory.Delete(_out, recursive: true);
        Directory.CreateDirectory(_out);

        // A test project with one target framework, or one the spec names, gives its output folder in the same query.
        var evaluated = await PropertiesAsync("test.output", testProject, spec.Framework, "TargetFramework", "TargetFrameworks", "TargetDir");
        var framework = spec.Framework ?? Frameworks(evaluated).FirstOrDefault() ?? throw new RunException($"couldn't read the target framework of {testProject}");
        var targetDir = evaluated["TargetFramework"] == framework ? evaluated["TargetDir"] : (await PropertiesAsync($"test.output.{framework}", testProject, framework, "TargetDir"))["TargetDir"];
        var testDirectory = Path.TrimEndingDirectorySeparator(targetDir);
        if (testDirectory.Length == 0) throw new RunException($"couldn't read the output folder of {testProject}");
        // Locked before the build, so two runs for this test output don't build it at once, but runs for another framework or test project can.
        Directory.CreateDirectory(Path.GetDirectoryName(testDirectory)!);
        using var outputLock = OpenLock(testDirectory + ".mutants.lock", $"another run is using {testDirectory}");
        Console.WriteLine($"Building {Path.GetFileName(testProject)} for {framework}.");
        var build = await RunToolAsync("baseline+build", _dotnet, ["build", testProject, "-c", "Release", "-f", framework, "-v:q", "-nologo", "-tl:off"], _root, 1800);
        if (build.ExitCode != 0) throw new RunException($"the test project doesn't build: {FirstError(build.Text) ?? $"see {build.Log}"}");
        // The test project's properties and each library's csc arguments are separate queries, so they run at once.
        var propertiesQuery = PropertiesAsync("test", testProject, framework, "TargetFileName", "RunSettingsFilePath", "IsTestingPlatformApplication");
        var libraryQueries = edits.Select(e => e.Project).ToHashSet(_pathComparer).Select(project => LibraryAsync(project, framework, testDirectory)).ToArray();
        var properties = await propertiesQuery;
        if (properties["TargetFileName"].Length == 0) throw new RunException($"couldn't read the assembly name of {testProject}");
        _testAssembly = properties["TargetFileName"];
        _runSettings = properties["RunSettingsFilePath"] is { Length: > 0 } runSettings ? Path.GetFullPath(runSettings, Path.GetDirectoryName(testProject)!) : null;
        _mtp = properties["IsTestingPlatformApplication"].Equals("true", StringComparison.OrdinalIgnoreCase);
        _treeNodeFilter = _mtp && File.Exists(Path.Combine(testDirectory, "TUnit.Engine.dll"));
        if (_mtp && !File.Exists(Path.Combine(testDirectory, "Microsoft.Testing.Extensions.TrxReport.dll")))
            throw new RunException($"{Path.GetFileName(testProject)} runs on Microsoft.Testing.Platform without its TRX report, which the run reads results from: add the Microsoft.Testing.Extensions.TrxReport package");

        var libraries = (await Task.WhenAll(libraryQueries)).ToDictionary(l => l.Project, _pathComparer);
        var sources = libraries.Values.SelectMany(l => l.SourceIndexes.Keys).ToHashSet(_pathComparer).ToDictionary(s => s, File.GetLastWriteTimeUtc, _pathComparer);
        var mutants = new Mutant[edits.Length];

        DeleteSlots(testDirectory);
        _slots = new string[Math.Min(parallel, mutants.Length)];
        string? rerun = null;
        TestOutcome baseline;
        int atATime;
        try
        {
            // The mutated copies are written in here, so a mutant that fails doesn't leave the copies written before it.
            var trees = new Dictionary<string, SyntaxNode>(_pathComparer);
            for (var k = 0; k < edits.Length; k++)
            {
                var edit = edits[k];
                var library = libraries[edit.Project];
                if (!library.SourceIndexes.TryGetValue(edit.File, out var index)) throw new RunException($"mutant {edit.Spec.Id}: {Path.GetRelativePath(_root, edit.File)} isn't compiled into {Path.GetFileName(edit.Project)}");
                var source = Path.Combine(_out, "mutants", edit.Spec.Id, Path.GetFileName(edit.File));
                var mutatedText = string.Concat(edit.Text.AsSpan(0, edit.Index), edit.Replacement, edit.Text.AsSpan(edit.Index + edit.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.WriteAllText(source, mutatedText, edit.Encoding);
                if (!trees.TryGetValue(edit.File, out var tree)) trees.Add(edit.File, tree = CSharpSyntaxTree.ParseText(edit.Text, library.ParseOptions).GetRoot());
                mutants[k] = new Mutant(edit.Spec, library, index, source, Risk(tree, mutatedText, edit.Index, edit.Length, library.ParseOptions));
                if (mutants[k].Warning is { } warning) Console.WriteLine($"warning: mutant {edit.Spec.Id} {warning}");
            }

            for (var k = 0; k < _slots.Length; k++) _slots[k] = $"{testDirectory}.mutant{k + 1}";
            Parallel.ForEach(_slots, slot => CopyDirectory(testDirectory, slot));
            // The baseline runs with the libraries compiled the way the mutants will be.
            foreach (var library in libraries.Values)
            {
                var start = Environment.TickCount64;
                var (assembly, error) = await CompileAsync($"original+{Path.GetFileNameWithoutExtension(library.Project)}", library, -1, null);
                _compileSeconds = Math.Max(_compileSeconds, SecondsSince(start));
                if (assembly is null) throw new RunException($"{Path.GetFileName(library.Project)} doesn't compile unchanged from its csc arguments: {error}");
                _originals.Add(library.Project, assembly);
                foreach (var slot in _slots) CopyAssembly(assembly, slot);
            }
            var (baselines, failed) = await BaselineAsync("baseline", filter, "mutants");
            var first = failed ?? baselines[0];
            if (first.Status == "TIMEOUT") throw new RunException($"the tests didn't finish in 30 minutes, see {first.Log}");
            if (RanNoTests(first)) throw new RunException($"the baseline ran no tests{(filter is null ? "" : " with the filter")}{Reason(first.Log)}, see {first.Log}");
            if (failed is not null)
            {
                var own = await TestsAsync("baseline+own-output", testDirectory, filter, 1800);
                throw new RunException(own.Status == "PASSED"
                    ? $"the tests pass in the test project's own output but not with the libraries compiled from their csc arguments ({failed.Status}{Names(failed.Failed, ": ")}), see {failed.Log}"
                    : $"the tests don't pass without a mutant ({own.Status}{Names(own.Failed, ": ")}), see {own.Log}. List a test that fails on purpose in exclude.");
            }
            baseline = baselines[0];
            // Each mutant also compiles, which takes about as long as the unchanged library did, and copies its assembly in and out.
            Console.WriteLine($"Testing {Plural(mutants.Length, "mutant")}, {_slots.Length} at a time, against {Plural(baseline.Total, "test")} that passed in {baseline.Seconds:0.0} s: {About(mutants.Length, baseline.Seconds + _compileSeconds + 0.5)}.");
            await TestMutantsAsync(mutants, "", filter, timeoutOption ?? TimeoutFor(baselines));
            atATime = _slots.Length;

            // Coverage counts every test in the project, so a member that only another test class tests looks covered, but its mutants
            // survive the filter. Rerunning the survivors against the whole project, or the rerun filter, tells those apart.
            var survivors = Array.FindAll(mutants, m => _results[m.Spec.Id].Status == "SURVIVED");
            var rerunFilter = spec.RerunFilter;
            if (filter is not null && filter != rerunFilter && survivors.Length > 0 && !filterOnly)
            {
                KeepSlots(Math.Min(survivors.Length, _slots.Length));
                var (whole, failedWhole) = await BaselineAsync("baseline+all", rerunFilter, "survivors");
                var firstWhole = failedWhole ?? whole[0];
                var scope = rerunFilter is null ? "the whole test project" : "the rerun filter's tests";
                if (RanNoTests(firstWhole))
                    Console.WriteLine($"warning: the rerun's baseline ran no tests{(rerunFilter is null ? "" : " with the rerun filter")}{Reason(firstWhole.Log)}, see {firstWhole.Log}, so the survivors weren't rerun.");
                else if (failedWhole is { Status: "TIMEOUT" })
                    Console.WriteLine($"warning: the rerun's baseline didn't finish in 30 minutes, see {failedWhole.Log}, so the survivors weren't rerun. Narrow the rerun with rerunFilter, or skip it with --filter-only.");
                else if (failedWhole is not null)
                    Console.WriteLine($"warning: {scope} failed without a mutant ({failedWhole.Status}{Names(failedWhole.Failed, ": ")}, see {failedWhole.Log}), so the survivors weren't rerun. List a test that fails on purpose in exclude.");
                else
                {
                    var tests = $"{(rerunFilter is null ? "all" : "the rerun filter's")} {Plural(whole[0].Total, "test")}";
                    Console.WriteLine($"Rerunning {Plural(survivors.Length, "survivor")} against {tests}, {_slots.Length} at a time: {About(survivors.Length, whole[0].Seconds + 0.5)}.");
                    // --timeout is sized for the filter, so it can only lengthen the rerun's.
                    await TestMutantsAsync(survivors, "+all", rerunFilter, Math.Max(timeoutOption ?? 0, TimeoutFor(whole)));
                    var killed = 0;
                    foreach (var survivor in survivors)
                    {
                        var result = _results[survivor.Spec.Id];
                        if (result.Status == "SURVIVED") continue;
                        _results[survivor.Spec.Id] = result with { Detail = result.Detail + ", outside the filter" };
                        if (result.Status is "KILLED" or "CRASHED") killed++;
                    }
                    rerun = $" {Plural(survivors.Length, "survivor")} rerun against {tests}: {killed} killed there.";
                }
            }
        }
        finally
        {
            DeleteSlots(testDirectory);
            TryDeleteDirectory(Path.Combine(_out, "compiled"));
            TryDeleteDirectory(Path.Combine(_out, "mutants"));
        }

        var changed = sources.Where(s => File.GetLastWriteTimeUtc(s.Key) != s.Value).Select(s => Path.GetRelativePath(_root, s.Key)).ToArray();
        if (changed.Length > 0) Console.WriteLine($"warning: {string.Join(", ", changed)} changed during the run, so the results may not match the code now");
        var results = Array.ConvertAll(mutants, m => (Mutant: m, Result: Expected(m.Spec, _results.GetValueOrDefault(m.Spec.Id) ?? new Result("ERROR", "no result", [], 0))));
        foreach (var (mutant, result) in results) Console.WriteLine($"{result.Status,-10} {mutant.Spec.Id}: {result.Detail} ({result.Seconds:0.0} s)");
        var report = Array.ConvertAll(results, r => new ReportEntry(r.Mutant.Spec.Id, r.Result.Status, r.Result.Detail, r.Mutant.Spec.Bug, r.Mutant.Spec.Expect, r.Mutant.Warning, r.Result.FailedTests, Math.Round(r.Result.Seconds, 1)));
        var merged = earlier is null ? report : Merge(earlier, report);
        File.WriteAllText(Path.Combine(_out, "results.json"), JsonSerializer.Serialize(merged, _reportOptions));
        if (merged.Length > report.Length) Console.WriteLine($"results.json keeps the earlier results of {Plural(merged.Length - report.Length, "other mutant")} too.");
        var counts = string.Join(", ", results.CountBy(r => r.Result.Status).Select(c => $"{c.Key} {c.Value}"));
        var excluded = _exclude.Length > 0 ? $" (failures of {string.Join(", ", _exclude)} don't count)" : "";
        var settings = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("CsCheck_", StringComparison.OrdinalIgnoreCase)).Order().Select(k => $", {k}={Environment.GetEnvironmentVariable(k)}");
        Console.WriteLine($"Baseline: {Plural(baseline.Total, "test")} passed in {baseline.Seconds:0.0} s{excluded}. {counts}.{rerun} {Plural(mutants.Length, "mutant")} in {SecondsSince(wall):0} s, {atATime} at a time, {(_mtp ? "Microsoft.Testing.Platform" : "VSTest")}{string.Concat(settings)}. Logs: {_out}");
        return 0;
    }

    static ReportEntry[]? EarlierResults(string path, MutantSpec[] all)
    {
        var ids = all.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ReportEntry[]>(File.ReadAllText(path), _reportOptions)?.Where(e => e?.Id is { } id && ids.Contains(id)).ToArray() : null; }
        catch (JsonException) { return null; }
    }

    // Each rerun mutant's result takes the place of its earlier one, and one that wasn't there before goes last.
    static ReportEntry[] Merge(ReportEntry[] earlier, ReportEntry[] rerun)
    {
        var byId = rerun.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var merged = Array.ConvertAll(earlier, e => byId.Remove(e.Id, out var r) ? r : e);
        return [.. merged, .. byId.Values];
    }

    // Everything in the run's folder but results.json and the logs and TRX files of the given mutants.
    void DeleteRunExcept(IEnumerable<string> ids)
    {
        var kept = ids.SelectMany(id => new[] { $"{id}.build.log", $"{id}.test.log", $"{id}+all.test.log", Path.Combine("trx", id), Path.Combine("trx", id + "+all") })
            .Append("results.json").Append("trx").Select(name => Path.Combine(_out, name)).ToHashSet(_pathComparer);
        var trx = Path.Combine(_out, "trx");
        foreach (var entry in Directory.GetFileSystemEntries(_out).Concat(Directory.Exists(trx) ? Directory.GetFileSystemEntries(trx) : []))
        {
            if (kept.Contains(entry)) continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
    }

    [GeneratedRegex(@" \| LineNumber: \d+ \| BytePositionInLine: \d+\.$")]
    static partial Regex JsonPosition { get; }

    static T ReadJson<T>(string path) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _specOptions) ?? throw new RunException($"{path} is empty"); }
        catch (JsonException e) { throw new RunException($"{path}{(e.LineNumber is { } line ? $" line {line + 1}" : "")}: {JsonPosition.Replace(e.Message, ".")}"); }
    }

    // A mutant given only by its id, with perhaps a bug and an expect of its own, takes the rest from the spec's candidates file.
    MutantSpec[] Resolve(Spec spec)
    {
        var candidates = new Dictionary<string, MutantEntry>(StringComparer.OrdinalIgnoreCase);
        if (spec.Candidates is { } name)
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(specPath)!, Portable(name)));
            if (!File.Exists(path)) throw new RunException($"{Path.GetFileName(specPath)} names a candidates file that isn't there: {path}");
            foreach (var candidate in ReadJson<CandidateFile>(path).Mutants)
            {
                if (candidate is not null) candidates[candidate.Id] = candidate;
            }
        }
        return Array.ConvertAll(spec.Mutants, m =>
        {
            var written = m.File is not null || m.Find is not null || m.Replace is not null;
            var source = written ? m : candidates.GetValueOrDefault(m.Id)
                ?? throw new RunException(spec.Candidates is null ? $"mutant {m.Id} needs a bug, a file, a find and a replace" : $"mutant {m.Id} isn't in {spec.Candidates}, so it needs a bug, a file, a find and a replace");
            if ((m.Bug ?? source.Bug) is not { } bug || source.File is not { } file || source.Find is not { } find || source.Replace is not { } replace)
                throw new RunException($"mutant {m.Id} needs a bug, a file, a find and a replace");
            return new MutantSpec(m.Id, bug, file, find, replace, m.Expect);
        });
    }

    // A survivor that its expected killer let through points at a test too weak to see the bug, and a kill by other tests at a misread one.
    static Result Expected(MutantSpec mutant, Result result)
    {
        if (mutant.Expect is not { } expect) return result;
        var expected = SplitNames(expect);
        return result.Status switch
        {
            "SURVIVED" => result with { Detail = $"{result.Detail} (expected {expect} to kill it)" },
            "KILLED" when !Array.Exists(result.FailedTests, test => Matches(test, expected)) => result with { Detail = $"{result.Detail}, not {expect} as expected" },
            _ => result,
        };
    }

    string About(int count, double secondsEach)
    {
        var seconds = Math.Ceiling(count / (double)_slots.Length) * secondsEach;
        return seconds < 90 ? $"about {seconds:0} s" : $"about {seconds / 60:0} min";
    }

    // Ids name files and folders, so none can be '..' or end in a dot, which Windows drops, or have the '+' the run's own names have.
    [GeneratedRegex(@"^\w([\w.-]*\w)?\z")]
    static partial Regex ValidId { get; }

    Edit Prepare(MutantSpec mutant, string testProject)
    {
        var id = mutant.Id;
        if (!ValidId.IsMatch(id)) throw new RunException($"mutant id '{id}' must be letters, digits, '.', '-' or '_', and start and end with a letter, digit or '_'");
        var file = Path.GetFullPath(Path.Combine(_root, Portable(mutant.File)));
        if (!File.Exists(file)) throw new RunException($"mutant {id}: there's no {mutant.File}");
        if (!_files.TryGetValue(file, out var read)) _files.Add(file, read = (ReadSource(file), FindProject(file)));
        var ((text, encoding), project) = read;
        if (mutant.Find.Length == 0) throw new RunException($"mutant {id} has an empty find");
        if (encoding.CodePage == Encoding.Latin1.CodePage && !Ascii.IsValid(mutant.Find + mutant.Replace)) throw new RunException($"mutant {id}: {mutant.File} isn't UTF-8, so its find and replace must be ASCII");
        // A \n in find matches the file's line ending at that point, so a file that mixes CRLF and LF lines still matches. One that starts
        // the find mustn't also match at a CRLF's \n, which would count it twice.
        var parts = mutant.Find.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var pattern = new Regex((parts[0].Length == 0 ? @"(?<!\r)" : "") + string.Join(@"\r?\n", parts.Select(Regex.Escape)));
        var found = new List<Match>();
        for (var match = pattern.Match(text); match.Success; match = pattern.Match(text, match.Index + 1)) found.Add(match);
        if (found.Count != 1)
            throw new RunException($"mutant {id}: the find text occurs {found.Count} times in {mutant.File}" + (found.Count > 1 ? $", on lines {string.Join(", ", found.Select(m => text.AsSpan(0, m.Index).Count('\n') + 1).ToHashSet())}: add surrounding text to make it unique" : ""));
        var find = found[0].Value;
        var newline = find.Contains("\r\n", StringComparison.Ordinal) || !find.Contains('\n') && text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replace = mutant.Replace.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
        if (find == replace) throw new RunException($"mutant {id}: find and replace are the same");
        if (_pathComparer.Equals(project, testProject)) throw new RunException($"mutant {id} is in the test project; mutate a library it references");
        // The risk check looks at what the edit changes, not the context around it that makes the find unique.
        var prefix = find.AsSpan().CommonPrefixLength(replace);
        var suffix = 0;
        while (suffix < find.Length - prefix && suffix < replace.Length - prefix && find[^(suffix + 1)] == replace[^(suffix + 1)]) suffix++;
        return new Edit(mutant, file, project, text, replace[prefix..^suffix], found[0].Index + prefix, find.Length - prefix - suffix, encoding);
    }

    // A file that isn't UTF-8 is read and written back byte for byte as Latin-1, so the copy compiles in the same code page as the original.
    static (string Text, Encoding Encoding) ReadSource(string file)
    {
        using var reader = new StreamReader(file, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
        try { return (reader.ReadToEnd(), reader.CurrentEncoding); }
        catch (DecoderFallbackException) { return (Encoding.Latin1.GetString(File.ReadAllBytes(file)), Encoding.Latin1); }
    }

    // C# compiles a const, an enum value and a default parameter value into each caller, and the test assembly isn't recompiled.
    static string? Risk(SyntaxNode root, string mutatedText, int start, int length, CSharpParseOptions options)
    {
        // An edit at the very end of the file starts past its last character.
        if (root.FindTrivia(Math.Min(start, Math.Max(0, root.FullSpan.End - 1))).IsKind(SyntaxKind.DisabledTextTrivia)) return "is in code an #if leaves out of this build, so it changes nothing";
        if (SyntaxFactory.AreEquivalent(root, CSharpSyntaxTree.ParseText(mutatedText, options).GetRoot(), topLevel: false)) return "only changes whitespace or comments, so it changes nothing";
        var span = Changed(root.SyntaxTree.GetText(), mutatedText, start, length);
        // The ; a change ends with is its declaration's or statement's, so the change is judged by what comes before it, such as an arrow
        // body emptied to { }.
        if (root.FindToken(span.End - 1) is { RawKind: (int)SyntaxKind.SemicolonToken } semicolon && semicolon.SpanStart > span.Start && span.End >= semicolon.Span.End)
            span = TextSpan.FromBounds(span.Start, semicolon.SpanStart);
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        if (node.FirstAncestorOrSelf<ParameterSyntax>() is { Default: { } value } parameter && value.Span.IntersectsWith(span)
            && parameter.Parent?.Parent is BaseMethodDeclarationSyntax or IndexerDeclarationSyntax or DelegateDeclarationSyntax or TypeDeclarationSyntax && !IsPrivate(parameter.Parent.Parent))
            return "changes a parameter's default value, which C# compiles into each caller, so calls from the tests keep the old one";
        if (node.FirstAncestorOrSelf<FieldDeclarationSyntax>() is { } field && field.Modifiers.Any(SyntaxKind.ConstKeyword) && !IsPrivate(field))
            return "changes a const, which C# compiles into each caller, so the tests and other libraries keep the old value";
        if (node.FirstAncestorOrSelf<EnumMemberDeclarationSyntax>() is { Parent: EnumDeclarationSyntax enumDeclaration } && !IsPrivate(enumDeclaration))
            return "changes an enum member's value, which C# compiles into each caller, so the tests keep the old one";
        return node.AncestorsAndSelf().Any(a => a is BlockSyntax or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or ConstructorInitializerSyntax or PrimaryConstructorBaseTypeSyntax or GlobalStatementSyntax or AttributeListSyntax)
            ? null
            : "is outside a method body, and the tests aren't recompiled, so a changed signature fails when they run";
    }

    // What an edit changes: its find without the text it shares with its replace at either end, so a find written out to a declaration's ;
    // for uniqueness is judged by the value it changes. An insertion is judged by the character before it.
    static TextSpan Changed(SourceText text, string mutated, int start, int length)
    {
        var replaced = mutated.Length - text.Length + length;
        var prefix = 0;
        while (prefix < length && prefix < replaced && text[start + prefix] == mutated[start + prefix]) prefix++;
        var suffix = 0;
        while (suffix < length - prefix && suffix < replaced - prefix && text[start + length - 1 - suffix] == mutated[start + replaced - 1 - suffix]) suffix++;
        var from = start + prefix;
        var to = start + length - suffix;
        return from < to ? TextSpan.FromBounds(from, to) : new TextSpan(Math.Max(0, from - 1), 1);
    }

    // A private member is only used inside its library, which is recompiled, so a mutant there takes full effect.
    static bool IsPrivate(SyntaxNode declaration) => declaration is MemberDeclarationSyntax member
        && (member.Modifiers.Any(SyntaxKind.PrivateKeyword) && !member.Modifiers.Any(SyntaxKind.ProtectedKeyword)
            || !member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))
                && member.Parent is TypeDeclarationSyntax and not InterfaceDeclarationSyntax);

    // A project's target framework, or each of its target frameworks.
    static string[] Frameworks(Dictionary<string, string> properties) =>
        properties["TargetFramework"] is { Length: > 0 } framework ? [framework] : properties["TargetFrameworks"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex(@"^/(out|pdb|refout|doc|errorlog|generatedfilesout):|^/warnaserror\+?(:|$)")]
    static partial Regex DroppedArgument { get; }

    async Task<Library> LibraryAsync(string project, string testFramework, string testDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        var frameworks = Frameworks(await PropertiesAsync($"{name}.frameworks", project, null, "TargetFramework", "TargetFrameworks"));
        var framework = frameworks.Length == 1 ? frameworks[0]
            : Array.Exists(frameworks, f => f == testFramework) ? testFramework
            : await FrameworkCopiedAsync(name, project, frameworks, testDirectory)
                ?? throw new RunException($"couldn't tell which of {name}'s target frameworks ({string.Join(", ", frameworks)}) the test project uses: none of their builds matches the copy in its output");
        var json = await MSBuildAsync($"{name}.csc", project, framework, ["-t:Compile", "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-p:NonExistentFile=__NonExistentFile__", "-getItem:CscCommandLineArgs", "-getProperty:TargetPath", "-getProperty:RoslynTargetsPath"]);
        var targetPath = json["Properties"]?["TargetPath"]?.GetValue<string>() ?? "";
        if (!File.Exists(Path.Combine(testDirectory, Path.GetFileName(targetPath)))) throw new RunException($"the test project doesn't reference {name}");
        var compiler = Path.GetFullPath(Path.Combine(json["Properties"]?["RoslynTargetsPath"]?.GetValue<string>() ?? "", "bincore", "csc.dll"));
        if (!File.Exists(compiler)) throw new RunException($"there's no compiler at {compiler}");
        if (json["Items"]?["CscCommandLineArgs"] is not JsonArray items || items.Count == 0) throw new RunException($"MSBuild gave no csc arguments for {name}");

        var directory = Path.GetDirectoryName(project)!;
        var kept = new List<string>(items.Count + 1);
        var sourceIndexes = new Dictionary<string, int>(_pathComparer);
        string[] defines = [];
        var languageVersion = LanguageVersion.Preview;
        foreach (var item in items)
        {
            var argument = item?["Identity"]?.GetValue<string>() ?? "";
            // Outputs other than the assembly would land in the user's obj folder, and warnings as errors would stop a mutant that drops a null check.
            if (argument.Length == 0 || argument == "/noconfig" || DroppedArgument.IsMatch(argument)) continue;
            if (argument.StartsWith("/define:", StringComparison.Ordinal)) defines = argument["/define:".Length..].Trim('"').Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
            if (argument.StartsWith("/langversion:", StringComparison.Ordinal) && LanguageVersionFacts.TryParse(argument["/langversion:".Length..], out var version)) languageVersion = version;
            if (IsSource(argument)) sourceIndexes[Path.GetFullPath(Path.Combine(directory, argument.Trim('"')))] = kept.Count;
            kept.Add(argument);
        }
        kept.Add("/skipanalyzers+");
        return new Library(project, directory, compiler, Path.GetFileName(targetPath), [.. kept], new CSharpParseOptions(languageVersion, DocumentationMode.None, SourceCodeKind.Regular, defines), sourceIndexes);
    }

    // csc's switches start with '/' or '-', but on Linux and macOS so does an absolute path, such as a linked or generated file.
    static bool IsSource(string argument) => argument[0] is not ('/' or '-' or '@')
        || (argument[0] == '/' && !OperatingSystem.IsWindows() && argument.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(argument));

    // A library with several target frameworks gives the test project the build nearest its own, which is the one that matches the
    // copy in the test output.
    async Task<string?> FrameworkCopiedAsync(string name, string project, string[] frameworks, string testDirectory)
    {
        foreach (var framework in frameworks)
        {
            var targetPath = (await PropertiesAsync($"{name}.{framework}", project, framework, "TargetPath"))["TargetPath"];
            var copy = Path.Combine(testDirectory, Path.GetFileName(targetPath));
            if (File.Exists(targetPath) && File.Exists(copy) && File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(File.ReadAllBytes(copy))) return framework;
        }
        return null;
    }

    async Task<(string? Assembly, string? Error)> CompileAsync(string name, Library library, int sourceIndex, string? source)
    {
        var directory = Path.Combine(_out, "compiled", name);
        Directory.CreateDirectory(directory);
        var assembly = Path.Combine(directory, library.AssemblyFileName);
        string[] lines = [.. library.Arguments, "/out:" + Quote(assembly)];
        if (source is not null) lines[sourceIndex] = Quote(source);
        var responseFile = Path.Combine(directory, "csc.rsp");
        await File.WriteAllLinesAsync(responseFile, lines);
        var run = await RunToolAsync($"{name}.build", _dotnet, ["exec", library.Compiler, "/noconfig", "/shared", "/nologo", "@" + responseFile], library.Directory, 600);
        if (run.ExitCode == 0) return (assembly, null);
        return (null, run.ExitCode is null ? "the compiler took over 600 s" : FirstError(run.Text) ?? $"see {run.Log}");
    }

    async Task TestMutantsAsync(Mutant[] batch, string suffix, string? filter, int timeout)
    {
        var queue = new ConcurrentQueue<Mutant>(batch);
        var done = 0;
        await Task.WhenAll(Array.ConvertAll(_slots, slot => Task.Run(async () =>
        {
            while (queue.TryDequeue(out var mutant))
            {
                var start = Environment.TickCount64;
                Result result;
                try { result = await TestMutantAsync(mutant, suffix, slot, filter, timeout); }
                catch (Exception e) { result = new Result("ERROR", e.Message, [], 0); }
                // Only a survivor is rerun, and it reruns the assembly it compiled.
                if (result.Status != "SURVIVED") TryDeleteDirectory(Path.Combine(_out, "compiled", mutant.Spec.Id));
                var earlier = _results.TryGetValue(mutant.Spec.Id, out var previous) ? previous.Seconds : 0;
                _results[mutant.Spec.Id] = result with { Seconds = earlier + SecondsSince(start) };
                Console.WriteLine($"  {Interlocked.Increment(ref done)}/{batch.Length} {result.Status} {mutant.Spec.Id}");
            }
        })));
    }

    async Task<Result> TestMutantAsync(Mutant mutant, string suffix, string slot, string? filter, int timeout)
    {
        // A survivor's rerun tests the assembly its first run compiled.
        var (assembly, error) = suffix.Length == 0 ? await CompileAsync(mutant.Spec.Id, mutant.Library, mutant.SourceIndex, mutant.Source) : (Path.Combine(_out, "compiled", mutant.Spec.Id, mutant.Library.AssemblyFileName), null);
        if (assembly is null) return new("NO_COMPILE", error!, [], 0);
        CopyAssembly(assembly, slot);
        try
        {
            var outcome = await TestsAsync(mutant.Spec.Id + suffix, slot, filter, timeout);
            return outcome.Status switch
            {
                "PASSED" => new("SURVIVED", mutant.Spec.Bug, [], 0),
                "KILLED" => new("KILLED", "by " + Names(outcome.Failed, ""), outcome.Failed, 0),
                "TIMEOUT" => new("TIMEOUT", $"no result in {timeout} s", [], 0),
                "CRASHED" => new("CRASHED", "the test host crashed", [], 0),
                _ => new("ERROR", $"see {outcome.Log}", [], 0),
            };
        }
        finally
        {
            CopyAssembly(_originals[mutant.Library.Project], slot);
        }
    }

    // The baseline runs in every copy at once, so tests that clash when run side by side, such as on a fixed port or a shared cache
    // file, fail here rather than killing mutants. If they pass alone, the run drops to one copy. A run that timed out isn't retried.
    async Task<(TestOutcome[] Baselines, TestOutcome? Failed)> BaselineAsync(string prefix, string? filter, string what)
    {
        var runs = new Task<TestOutcome>[_slots.Length];
        for (var k = 0; k < runs.Length; k++) runs[k] = TestsAsync($"{prefix}+{k + 1}", _slots[k], filter, 1800);
        var baselines = await Task.WhenAll(runs);
        var failed = Array.Find(baselines, b => b.Status != "PASSED");
        if (failed is null || _slots.Length == 1 || failed.Status == "TIMEOUT") return (baselines, failed);
        var alone = await TestsAsync(prefix + "+alone", _slots[0], filter, 1800);
        if (alone.Status != "PASSED") return (baselines, alone);
        var which = failed.Failed.Length > 0 ? Names(failed.Failed, "") : $"the tests ({failed.Status})";
        var keep = failed.Failed.Length == 0 ? "" : failed.Failed.Length == 1 ? $". To keep {_slots.Length} at a time, list that test in the spec's exclude, unless it tests the mutated code" : $". To keep {_slots.Length} at a time, list those tests in the spec's exclude, unless they test the mutated code";
        Console.WriteLine($"warning: {which} failed when {_slots.Length} runs shared the machine but passed alone (see {failed.Log}), so the {what} run one at a time{keep}");
        KeepSlots(1);
        return ([alone], null);
    }

    void KeepSlots(int n)
    {
        foreach (var slot in _slots.AsSpan(n)) TryDeleteDirectory(slot);
        _slots = _slots[..n];
    }

    // A filter that matches no test passes under VSTest.
    static bool RanNoTests(TestOutcome baseline) => baseline.Total == 0 && baseline.Status is not ("CRASHED" or "TIMEOUT");

    static int TimeoutFor(TestOutcome[] baselines) => Math.Max(30, (int)Math.Ceiling(5 * baselines.Max(b => b.Seconds)));

    static double SecondsSince(long start) => (Environment.TickCount64 - start) / 1000.0;

    async Task<TestOutcome> TestsAsync(string name, string directory, string? filter, int timeout)
    {
        var results = Path.Combine(_out, "trx", name);
        var assembly = Path.Combine(directory, _testAssembly);
        string[] arguments = _mtp
            ? ["exec", assembly, "--results-directory", results, "--report-trx", "--report-trx-filename", name + ".trx"]
            : ["test", assembly, "--nologo", "--logger", $"trx;LogFileName={name}.trx", "--results-directory", results];
        if (filter is not null) arguments = [.. arguments, .. FilterArguments(filter)];
        if (_runSettings is not null && !_mtp) arguments = [.. arguments, "--settings", _runSettings];
        var run = await RunToolAsync(name + ".test", _dotnet, arguments, directory, timeout);
        var (total, all) = ReadTrx(Path.Combine(results, name + ".trx"));
        var failed = Array.FindAll(all, test => !Matches(test, _exclude));
        var crashed = _mtp ? run.ExitCode is 3 or 7 : run.Text.Contains("Test host process crashed", StringComparison.OrdinalIgnoreCase) || run.Text.Contains("test run was aborted", StringComparison.OrdinalIgnoreCase);
        var status = run.ExitCode is null ? "TIMEOUT"
            : failed.Length > 0 ? "KILLED"
            : run.ExitCode == 0 ? "PASSED"
            : crashed ? "CRASHED"
            : total > 0 && all.Length > 0 ? "PASSED"
            : "ERROR";
        return new(status, failed, total, run.Seconds, run.Log);
    }

    // A filter that starts with -- is passed as the test app's own arguments, such as xUnit v3's --filter-class.
    string[] FilterArguments(string filter) => filter.StartsWith("--", StringComparison.Ordinal)
        ? filter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        : [_treeNodeFilter ? "--treenode-filter" : "--filter", filter];

    public static string[] SplitNames(string names) => names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // TRX names a test by its method (NUnit, TUnit) or its full name (xUnit), with any data arguments after it, spaced or not (MSTest).
    static bool Matches(string test, string[] names)
    {
        var paren = test.IndexOf('(');
        var method = paren < 0 ? test.AsSpan() : test.AsSpan(0, paren).TrimEnd();
        foreach (var name in names)
        {
            if (method.Equals(name, StringComparison.Ordinal) || method.Length > name.Length && method[^(name.Length + 1)] == '.' && method.EndsWith(name, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    static (int Total, string[] Failed) ReadTrx(string path)
    {
        if (!File.Exists(path)) return (0, []);
        var root = XDocument.Load(path).Root;
        var total = (int?)root?.Element(_trx + "ResultSummary")?.Element(_trx + "Counters")?.Attribute("total") ?? 0;
        // Sorted, since the test host writes results in no fixed order and Names shows only the first three.
        var failed = root?.Descendants(_trx + "UnitTestResult")
            .Where(r => (string?)r.Attribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted")
            .Select(r => (string?)r.Attribute("testName") ?? "?")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return (total, failed ?? []);
    }

    async Task<Dictionary<string, string>> PropertiesAsync(string name, string project, string? framework, params string[] names)
    {
        var json = await MSBuildAsync(name, project, framework, Array.ConvertAll(names, n => "-getProperty:" + n));
        return names.ToDictionary(n => n, n => json["Properties"]?[n]?.GetValue<string>() ?? "");
    }

    // MSBuild prints JSON when asked for more than one property, so MSBuildProjectFullPath is always asked for too.
    async Task<JsonObject> MSBuildAsync(string name, string project, string? framework, string[] options)
    {
        string[] arguments = ["msbuild", project, "-nologo", "-p:Configuration=Release", .. (framework is null ? Array.Empty<string>() : [$"-p:TargetFramework={framework}"]), "-getProperty:MSBuildProjectFullPath", .. options];
        var run = await RunToolAsync($"msbuild+{name}", _dotnet, arguments, _root, 600);
        var output = run.StandardOutput;
        // The JSON starts on a line of its own, and a warning can come before it.
        var start = output.StartsWith('{') ? 0 : output.IndexOf("\n{", StringComparison.Ordinal) is >= 0 and var at ? at + 1 : -1;
        var end = output.LastIndexOf('}');
        if (run.ExitCode != 0 || start < 0 || end < start) throw new RunException($"MSBuild couldn't read {Path.GetFileName(project)}: {FirstError(run.Text) ?? $"see {run.Log}"}");
        return JsonNode.Parse(output[start..(end + 1)]) as JsonObject ?? throw new RunException($"MSBuild gave no JSON for {Path.GetFileName(project)}, see {run.Log}");
    }

    async Task<ToolRun> RunToolAsync(string name, string file, string[] arguments, string directory, int timeoutSeconds)
    {
        var log = Path.Combine(_out, name + ".log");
        var startInfo = new ProcessStartInfo(file, arguments) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // English output, so a crashed test host is recognised, and no diff tool opens when a snapshot test fails.
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["VSLANG"] = "1033";
        startInfo.Environment["DiffEngine_Disabled"] = "true";
        var standardOutput = new StringBuilder();
        var text = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) lock (text) { standardOutput.AppendLine(e.Data); text.AppendLine(e.Data); }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) lock (text) text.AppendLine(e.Data);
        };
        var start = Environment.TickCount64;
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception e) { throw new RunException($"couldn't start {file}: {e.Message}"); }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // WaitForExit with a timeout doesn't wait for the output to close, which a compiler server or build node that outlives the
        // process can hold open, so the output gets a few seconds' grace after the process exits.
        var exited = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception) { }
            process.WaitForExit(30_000);
        }
        // WaitForExitAsync also waits for both outputs to close.
        using var grace = new CancellationTokenSource(5000);
        try { await process.WaitForExitAsync(grace.Token); }
        catch (OperationCanceledException) { }
        string all, stdout;
        lock (text)
        {
            all = StripAnsi(text.ToString());
            stdout = StripAnsi(standardOutput.ToString());
        }
        await File.WriteAllTextAsync(log, all);
        return new ToolRun(exited ? process.ExitCode : null, stdout, all, log, SecondsSince(start));
    }

    string FindProject(string file)
    {
        for (var directory = Path.GetDirectoryName(file); directory is not null && directory.Length >= _root.Length; directory = Path.GetDirectoryName(directory))
        {
            var projects = Directory.GetFiles(directory, "*.csproj");
            if (projects.Length > 1) throw new RunException($"there's more than one .csproj in {directory}");
            if (projects.Length == 1) return projects[0];
        }
        throw new RunException($"there's no .csproj above {file}");
    }

    static string ResolveProject(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return path;
        var projects = Directory.Exists(path) ? Directory.GetFiles(path, "*.csproj") : [];
        return projects.Length == 1 ? projects[0] : throw new RunException($"expected one .csproj in {path}, found {projects.Length}");
    }

    static FileStream OpenLock(string path, string busy)
    {
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException e) when (e is not DirectoryNotFoundException) { throw new RunException(busy); }
    }

    static void DeleteSlots(string testDirectory)
    {
        foreach (var slot in Directory.GetDirectories(Path.GetDirectoryName(testDirectory)!, Path.GetFileName(testDirectory) + ".mutant*")) TryDeleteDirectory(slot);
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        Parallel.ForEach(Directory.GetFiles(from, "*", SearchOption.AllDirectories), file =>
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            File.Copy(file, target, overwrite: true);
            // A read-only file, such as one from a Perforce workspace, would stop the copy being deleted on Windows.
            File.SetAttributes(target, FileAttributes.Normal);
        });
    }

    static void CopyAssembly(string assembly, string directory)
    {
        if (!File.Exists(assembly)) throw new RunException($"{assembly} is gone, so {directory} can't be given it");
        string[] files = [assembly, Path.ChangeExtension(assembly, ".pdb")];
        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var target = Path.Combine(directory, Path.GetFileName(file));
            // The test host can hold the old copy open for a moment after its run exits.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Copy(file, target, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 300) { Thread.Sleep(200); }
            }
        }
    }

    static void TryDeleteDirectory(string directory)
    {
        for (var attempt = 1; attempt <= 25 && Directory.Exists(directory); attempt++)
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    static string DotnetHost()
    {
        var host = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        return File.Exists(host) ? host : "dotnet";
    }

    static string Names(string[] tests, string prefix) =>
        tests.Length == 0 ? "" : prefix + string.Join(", ", tests.Take(3)) + (tests.Length > 3 ? $" and {tests.Length - 3} more" : "");

    [GeneratedRegex(@"(?m)([^\\/\r\n]+\(\d+,\d+\): error \w+: [^\r\n]*?)(?: \[[^\]\r\n]*\])?\r?$")]
    static partial Regex LocatedError { get; }

    [GeneratedRegex(@"(?m)^[^\r\n]*\berror [A-Z]+\d+: [^\r\n]*")]
    static partial Regex PlainError { get; }

    static string? FirstError(string text)
    {
        var located = LocatedError.Match(text);
        if (located.Success) return located.Groups[1].Value;
        var plain = PlainError.Match(text);
        return plain.Success ? plain.Value.Trim() : null;
    }

    [GeneratedRegex(@"(?i)\b(error|unknown|invalid)\b")]
    static partial Regex ReasonWord { get; }

    // The first line of a log that says why, such as a test app rejecting its arguments.
    static string Reason(string log)
    {
        var line = File.Exists(log) ? File.ReadLines(log).FirstOrDefault(ReasonWord.IsMatch) : null;
        return line is null ? "" : $": {line.Trim()}";
    }

    static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // A spec written on Windows can use backslashes, which Linux and macOS take as part of a file name.
    static string Portable(string path) => OperatingSystem.IsWindows() ? path : path.Replace('\\', '/');

    static string Quote(string path) => '"' + path + '"';

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    static partial Regex Ansi { get; }

    static string StripAnsi(string text) => Ansi.Replace(text, "");
}
