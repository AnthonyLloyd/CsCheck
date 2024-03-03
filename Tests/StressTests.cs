namespace Tests;

using System;
using System.Diagnostics;
using CsCheck;

public class StressTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // can set env var in dotnet test now
    static Gen<List<string>> SubsetOf(List<string> items)
    {
        return Gen.Int[1, items.Count - 1].SelectMany(n => Gen.Shuffle(items, n));
    }

    [Fact]
    public async Task Stress_Test_Full_Suite()
    {
        const int timeout = 30;
        const int memoryLimit = 10 * 1024 * 1204;
        var testProject = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
        var tests = GetTestNames(testProject);
        tests.Remove($"{nameof(Tests)}.{nameof(StressTests)}.{nameof(Stress_Test_Full_Suite)}");
        var maxMemory = 0L;
        await SubsetOf(tests).SampleAsync(async tests =>
        {
            var args = $"test --nologo -v m --tl:off --no-build -c Release --filter {string.Join('|', tests)} {testProject}";
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = "dotnet",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var running = true;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => running = false;
            if (!process.Start()) throw new("Didn't start");
            long memory = 0;
            var timeoutTimestamp = timeout * Stopwatch.Frequency + Stopwatch.GetTimestamp();
            while (running && timeoutTimestamp > Stopwatch.GetTimestamp())
            {
                try
                {
                    memory = process.PeakWorkingSet64;
                }
                catch { }
                if (memory > memoryLimit)
                    Interlocked.Exchange(ref maxMemory, memory);
                if (memory > memoryLimit)
                {
                    var message = $"Memory limit exceeded: {(double)memory / (1024 * 1024):n2} MB";
                    process.Kill(true);
                    throw new(message);
                }
                await Task.Delay(100);
            }
            if (running)
                throw new("Timeout!");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            if (process.ExitCode != 0 || error.Length != 0)
                throw new($"ExitCode: {process.ExitCode}\n{output}\n{error}");
        }, time: 60);
        output.WriteLine($"MaxMemory: {(double)maxMemory / (1024 * 1024):n2} MB");
    }

    private static List<string> GetTestNames(string testProject)
    {
        var getTestList = Process.Start(new ProcessStartInfo()
        {
            FileName = "dotnet",
            Arguments = "test --nologo -v q --tl:off -c Release --list-tests " + testProject,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        if (getTestList is null)
            throw new($"{nameof(getTestList)} returned null");
        var stdout = getTestList.StandardOutput;
        if (stdout.EndOfStream) throw new("No stdout first line");
        var line = getTestList.StandardOutput.ReadLine();
        if (line?.StartsWith("Test run for ") != true) throw new($"First line: \"{line}\"");
        if (stdout.EndOfStream) throw new("No stdout second line");
        line = getTestList.StandardOutput.ReadLine();
        if (line != "The following Tests are available:") throw new($"Second line: \"{line}\"");

        var tests = new List<string>();
        while (!getTestList.StandardOutput.EndOfStream)
        {
            line = getTestList.StandardOutput.ReadLine();
            if (line is null) throw new("line null");
            tests.Add(line.Trim());
        }
        return tests;
    }
}