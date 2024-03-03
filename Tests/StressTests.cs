namespace Tests;

using System;
using System.Diagnostics;
using CsCheck;

public class StressTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact(Skip = "don't normally run the stress test")]
    public async Task Stress_Test()
    {
        const int timeout = 30;
        var limit = Environment.GetEnvironmentVariable("Stress_Memory_Limit");
        var memoryLimit = (string.IsNullOrEmpty(limit) ? 150 : int.Parse(limit)) * 1024 * 1204;
        var testProject = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
        var tests = GetTestNames(testProject);
        tests.Remove($"{nameof(Tests)}.{nameof(StressTests)}.{nameof(Stress_Test)}");
        //tests.Remove(tests.First(i => i.EndsWith("DbgWalkthrough")));
        var maxMemory = 0L;
        int timeoutProcesses = 0, totalProcesses = 0;
        try
        {
            await Gen.Shuffle(tests, 1, tests.Count).SampleAsync(async tests =>
            {// --disable-build-servers --blame-hang-timeout {timeout}s
                var args = $"test --nologo --tl:off --no-build -c Release --filter {string.Join('|', tests)} {testProject}";
                using var process = StartDotnetProcess(args);
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var timeoutTimestamp = (timeout + 60) * Stopwatch.Frequency + Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() < timeoutTimestamp && !process.HasExited)
                {
                    long memory = 0;
                    try
                    {
                        memory = process.PeakWorkingSet64;
                    }
                    catch { }
                    if (memory > maxMemory)
                        Interlocked.Exchange(ref maxMemory, memory);
                    if (memory > memoryLimit)
                    {
                        var message = $"Memory limit exceeded: {(double)memory / (1024 * 1024):n2} MB";
                        process.Kill(true);
                        throw new(message);
                    }
                    await Task.Delay(100);
                    process.Refresh();
                }
                Interlocked.Increment(ref totalProcesses);
                if (!process.HasExited)
                {
                    process.Kill(true);
                    Interlocked.Increment(ref timeoutProcesses);
                }
                var output = await outputTask;
                var error = await errorTask;
                if (error.Length != 0)
                    throw new($"ExitCode: {process.ExitCode}\n{output}\n{error}");
            }, time: 60, threads: Environment.ProcessorCount / 2);
        }
        finally
        {
            output.WriteLine($"MaxMemory: {(double)maxMemory / (1024 * 1024):n2} MB");
            output.WriteLine($"Total Processes: {totalProcesses}");
            output.WriteLine($"Timeout Processes: {timeoutProcesses}");
        }
    }

    private static List<string> GetTestNames(string testProject)
    {
        using var process = StartDotnetProcess("test --nologo -v q --tl:off -c Release --list-tests " + testProject);
        var stdout = process.StandardOutput;
        if (stdout.EndOfStream) throw new("No stdout first line");
        var line = stdout.ReadLine();
        if (line?.StartsWith("Test run for ") != true) throw new($"First line: \"{line}\"");
        if (stdout.EndOfStream) throw new("No stdout second line");
        line = stdout.ReadLine();
        if (line != "The following Tests are available:") throw new($"Second line: \"{line}\"");
        var tests = new List<string>();
        while (!stdout.EndOfStream)
        {
            line = stdout.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) throw new("line null");
            tests.Add(line.Trim());
        }
        return tests;
    }

    private static Process StartDotnetProcess(string args)
    {
        var process = new Process
        {
            StartInfo = new()
            {
                FileName = "dotnet",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = null,
                StandardErrorEncoding = null,
            },
        };
        if (!process.Start()) throw new("Can't start process");
        return process;
    }
}