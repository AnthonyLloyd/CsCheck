namespace CsCheck;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

public interface ILogger : IDisposable
{
    Action<T> WrapAssert<T>(Action<T> assert);
    Func<T, bool> WrapAssert<T>(Func<T, bool> assert);
    Func<T, Task> WrapAssert<T>(Func<T, Task> assert);
    Func<T, Task<bool>> WrapAssert<T>(Func<T, Task<bool>> assert);
}

internal sealed class TycheLogger : ILogger
{
    private readonly Task _loggingTask;
    private readonly Channel<(object Value, bool Success)> _channel;

    public TycheLogger(Func<Task> loggingTask, Channel<(object Value, bool Success)> channel)
    {
        _loggingTask = Task.Run(loggingTask);
        _channel = channel;
    }

    public Action<T> WrapAssert<T>(Action<T> assert)
    {
        return t =>
        {
            try
            {
                assert(t);
                _channel.Writer.TryWrite((t ?? (object)string.Empty, true));
            }
            catch
            {
                _channel.Writer.TryWrite((t ?? (object)string.Empty, false));
                throw;
            }
        };
    }

    public Func<T, bool> WrapAssert<T>(Func<T, bool> assert)
    {
        return t =>
        {
            try
            {
                var result = assert(t);
                _channel.Writer.TryWrite((t ?? (object)string.Empty, result));
                return result;
            }
            catch
            {
                _channel.Writer.TryWrite((t ?? (object)string.Empty, false));
                throw;
            }
        };
    }

    public Func<T, Task> WrapAssert<T>(Func<T, Task> assert)
    {
        return async t =>
        {
            try
            {
                await assert(t);
                _channel.Writer.TryWrite((t ?? (object)string.Empty, true));
            }
            catch
            {
                _channel.Writer.TryWrite((t ?? (object)string.Empty, false));
                throw;
            }
        };
    }

    public Func<T, Task<bool>> WrapAssert<T>(Func<T, Task<bool>> assert)
    {
        return async t =>
        {
            try
            {
                var result = await assert(t);
                _channel.Writer.TryWrite((t ?? (object)string.Empty, result));
                return result;
            }
            catch
            {
                _channel.Writer.TryWrite((t ?? (object)string.Empty, false));
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
    public enum LogProcessor { Tyche }

    public static ILogger CreateLogger(LogProcessor p, [CallerMemberName] string? name = null, string? directory = null, StreamWriter? writer = null, Func<object, string>? print = null) => p switch
    {
        LogProcessor.Tyche => CreateTycheLogger(name, directory, writer, print),
        _ => throw new ArgumentOutOfRangeException(nameof(p), p, null),
    };

    public static ILogger CreateTycheLogger([CallerMemberName] string? name = null, string? directory = null, StreamWriter? writer = null, Func<object, string>? print = null)
    {
        if (name is null) throw new CsCheckException("name is null");
        var channel = Channel.CreateUnbounded<(object Value, bool Success)>(new() { SingleReader = true, SingleWriter = false });
        return new TycheLogger(async () =>
        {
            var todayString = $"{DateTime.Today.Date:yyyy-M-dd}";
            var runStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (writer is null)
            {
                directory ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../.cscheck/observed");
                Directory.CreateDirectory(directory);
                var infoFilePath = Path.Combine(directory, $"{todayString}_info.jsonl");
                var infoRecord = new TycheInfo("info", runStart, name, "Hypothesis Statistics", "");
                await using var infoWriter = new StreamWriter(infoFilePath, true);
                infoWriter.AutoFlush = true;
                await infoWriter.WriteLineAsync(JsonSerializer.Serialize(infoRecord, TycheJsonSerializerContext.Default.TycheInfo));
                infoWriter.Close();

                var testcasesFilePath = Path.Combine(directory, $"{todayString}_testcases.jsonl");
                var fileStream = new FileStream(testcasesFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(fileStream);
                writer.AutoFlush = true;
                await LogTycheTestCases(name, writer, channel, runStart, print);
                await writer.DisposeAsync();
            }
            else
            {
                writer.AutoFlush = true;
                await LogTycheTestCases(name, writer, channel, runStart, print);
            }
        }, channel);
    }

    private static async Task LogTycheTestCases(string propertyUnderTest, StreamWriter writer, Channel<(object Value, bool Success)> channel,
        double runStart, Func<object, string>? print = null)
    {
        while (await channel.Reader.WaitToReadAsync())
        {
            var (value, success) = await channel.Reader.ReadAsync();
            var tycheData = new TycheData("test_case", runStart, propertyUnderTest, success ? "passed" : "failed", (print ?? Check.Print)(value)
                , "reason", _emptyDictionary, "testing", _emptyDictionary, null, _emptyDictionary, _emptyDictionary);
            var serializedData = JsonSerializer.Serialize(tycheData, TycheJsonSerializerContext.Default.TycheData);
            await writer.WriteLineAsync(serializedData);
        }
    }

    private static readonly Dictionary<string, string> _emptyDictionary = [];
}

#pragma warning disable IDE1006 // Naming Styles
internal record TycheInfo(string type, double run_start, string property, string title, string content);

internal record TycheData(string type, double run_start, string property, string status, string representation,
    string? status_reason, Dictionary<string, string> arguments, string? how_generated, Dictionary<string, string> features,
    Dictionary<string, string>? coverage, Dictionary<string, string> timing, Dictionary<string, string> metadata);

[JsonSerializable(typeof(TycheInfo))]
[JsonSerializable(typeof(TycheData))]
internal partial class TycheJsonSerializerContext : JsonSerializerContext;