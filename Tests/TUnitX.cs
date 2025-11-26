namespace Tests;

internal static class TUnitX
{
    public static void WriteLine(string? message) => TestContext.Current?.OutputWriter.WriteLine(message);
}
