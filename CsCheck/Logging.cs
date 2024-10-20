namespace CsCheck;

using System.Text.Json;
using System.Threading.Channels;

public interface ILogger<T> : IDisposable
{
    Action<T> WrapAssert(Action<T> assert);
}

public sealed class TycheLogger<T> : ILogger<T>
{
    private readonly Task _loggingTask;
    private readonly Channel<(T Value, bool Success)> _channel;
    public TycheLogger(Func<Task> loggingTask, Channel<(T Value, bool Success)> channel)
    {
        _loggingTask = Task.Run(loggingTask);
        _channel = channel;
    }
    public Action<T> WrapAssert(Action<T> assert)
    {
        return t =>
        {
            try
            {
                assert(t);
                _channel.Writer.TryWrite((t, true));
            }
            catch
            {
                _channel.Writer.TryWrite((t, false));
                throw;
            }
        };
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _loggingTask.Wait();
    }
}

public static class Logging
{
    public enum LogProcessor
    {
        Tyche,
    }

    public static ILogger<T> CreateLogger<T>(LogProcessor p, string propertyUnderTest, StreamWriter? writer = null)
    {
        var channel = Channel.CreateUnbounded<(T Value, bool Success)>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            }
        );
        switch (p)
        {
            case LogProcessor.Tyche:
                var t = async () =>
                {
                    var todayString = $"{DateTime.Today.Date:yyyy-M-dd}";
                    var projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\.."));

                    var runStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                    if (writer == null)
                    {
                        var infoFilePath = Path.Combine(projectRoot, ".cscheck\\observed", $"{todayString}_info.jsonl");
                        Directory.CreateDirectory(Path.GetDirectoryName(infoFilePath)!);

                        //Log information that Tyche uses to distinguish between different runs
                        using var infoWriter = new StreamWriter(infoFilePath, true);
                        infoWriter.AutoFlush = true;

                        var infoRecord =
                            new
                            {
                                type = "info",
                                run_start = runStart,
                                property = propertyUnderTest,
                                title = "Hypothesis Statistics",
                                content = ""
                            };
                        await infoWriter.WriteLineAsync(JsonSerializer.Serialize(infoRecord)).ConfigureAwait(false);
                        infoWriter.Close();
                    }

                    if (writer != null)
                    {
                        writer.AutoFlush = true;
                        await LogTycheTestCases(propertyUnderTest, writer, channel, runStart).ConfigureAwait(false);
                    }
                    else
                    {
                        var testcasesFilePath = Path.Combine(projectRoot, $".CsCheck\\observed", $"{todayString}_testcases.jsonl");
                        Directory.CreateDirectory(Path.GetDirectoryName(testcasesFilePath)!);
                        using var w = new StreamWriter(testcasesFilePath, true);
                        w.AutoFlush = true;
                        await LogTycheTestCases(propertyUnderTest, w, channel, runStart).ConfigureAwait(false);
                    }
                };
                return new TycheLogger<T>(t, channel);
            default:
                throw new ArgumentOutOfRangeException(nameof(p), p, null);
        }
    }

    private static async Task LogTycheTestCases<T>(string propertyUnderTest, StreamWriter writer, Channel<(T Value, bool Success)> channel,
        double runStart)
    {
        while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var (value, success) = await channel.Reader.ReadAsync().ConfigureAwait(false);
            var tycheData = new TycheData(
                "test_case", runStart, propertyUnderTest,
                success ? "passed" : "failed", JsonSerializer.Serialize(value),
                "reason", d, "testing", d, null, d, d
            );
            var serializedData = JsonSerializer.Serialize(tycheData);
            await writer.WriteLineAsync(serializedData).ConfigureAwait(false);
        }
    }
}

#pragma warning disable IDE1006 // Naming Styles
public record TycheData(string type, double run_start, string property, string status, string representation,
    string? status_reason, Dictionary<string, string> arguments, string? how_generated, Dictionary<string, string> features,
    Dictionary<string, string>? coverage, Dictionary<string, string> timing, Dictionary<string, string> metadata);