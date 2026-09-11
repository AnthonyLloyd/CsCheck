namespace Tests;

using System.Text.RegularExpressions;

internal static class TUnitX
{
    public static void WriteLine(string? message) => TestContext.Current?.OutputWriter.WriteLine(message);
}

/// <summary>The fenced blocks of the guide and the readme, so the tests that own a report or a diagram can check they
/// still quote what the code produces. A hand copied example goes stale in silence, and two files quoting one report
/// go stale independently of each other.</summary>
internal static partial class Docs
{
    /// <summary>Every fenced block of docs/Spec.md, in document order, with line endings normalised to the generated
    /// form.</summary>
    public static IEnumerable<string> Spec => Fenced(_specMd);

    /// <summary>Every fenced block of README.md, which quotes some of the same output separately.</summary>
    public static IEnumerable<string> Readme => Fenced(_readme);

    /// <summary>A block wrapped by hand, rejoined into the single line the code emits.</summary>
    public static string Unwrap(string block)
        => string.Join(' ', block.Split('\n', StringSplitOptions.RemoveEmptyEntries));

    static readonly string _specMd = Read("docs", "Spec.md");
    static readonly string _readme = Read("README.md");

    static IEnumerable<string> Fenced(string text)
    {
        foreach (var m in MyRegex.Matches(text))
            yield return ((Match)m).Groups[1].Value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^```[a-z]*\r?\n(.*?)^```", RegexOptions.Singleline)]
    private static partial Regex MyRegex { get; }

    // Found by walking up from the test binary rather than from a compile time path, so it does not assume the tests
    // run on the machine that built them.
    static string Read(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(Path.Combine(parts) + " not found above " + AppContext.BaseDirectory);
    }
}
