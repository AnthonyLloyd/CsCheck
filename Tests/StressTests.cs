namespace Tests;

using System;
using System.Diagnostics;
using CsCheck;

public class StressTests()
{
    [Test, Skip("don't normally run the stress test")]
    public async Task Stress_Test()
    {
        const long oneMB = 1024 * 1024;
        const int hang_timeout = 120;
        var limit = Environment.GetEnvironmentVariable("Stress_Memory_Limit");
        var memoryLimit = (string.IsNullOrEmpty(limit) ? 150 : int.Parse(limit)) * oneMB;
        var testProject = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
        var args = $"test --nologo --tl:off --no-build -c Release {testProject} --filter ";
        var tests = GetTestNames(testProject);
        tests.Remove($"{nameof(Tests)}.{nameof(StressTests)}.{nameof(Stress_Test)}");
        long hungProcesses = 0, totalProcesses = 0, maxMemory = 0;
        try
        {
            await Gen.Shuffle(tests, 1, tests.Count).SampleAsync(async tests =>
            {
                using var process = StartDotnetProcess(args + string.Join('|', tests));
                var timeoutTimestamp = hang_timeout * Stopwatch.Frequency + Stopwatch.GetTimestamp();
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
                        process.Kill(true);
                        throw new($"Memory limit exceeded: {(double)memory / oneMB:n2} MB");
                    }
                    await Task.Delay(100);
                    process.Refresh();
                }
                Interlocked.Increment(ref totalProcesses);
                if (!process.HasExited)
                {
                    process.Kill(true);
                    Interlocked.Increment(ref hungProcesses);
                    throw new("Hang timeout!");
                }
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                if (error.Length != 0)
                    throw new($"ExitCode: {process.ExitCode}\n{output}\n{error}");
            }, time: 60, threads: 8);
        }
        finally
        {
            Console.WriteLine($"MaxMemory: {(double)maxMemory / oneMB:n2} MB");
            Console.WriteLine($"Total Processes: {totalProcesses}");
            Console.WriteLine($"Hung Processes: {hungProcesses}");
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