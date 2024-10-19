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

    public static ILogger<T> CreateLogger<T>(StreamWriter w, LogProcessor p, string propertyUnderTest)
    {
        w.AutoFlush = true;
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
                    while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
                    {
                        var d = new Dictionary<string, string>(StringComparer.Ordinal);
                        var (value, success) = await channel.Reader.ReadAsync().ConfigureAwait(false);
                        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                        var tycheData = new TycheData(
                            "test_case", timestamp, propertyUnderTest,
                            success ? "passed" : "failed", JsonSerializer.Serialize(value),
                            "reason", d, "testing", d, null, d, d
                        );
                        var serializedData = JsonSerializer.Serialize(tycheData);
                        await w.WriteLineAsync(serializedData).ConfigureAwait(false);
                    }
                };
                return new TycheLogger<T>(t, channel);
            default:
                throw new ArgumentOutOfRangeException(nameof(p), p, null);
        }
    }
}

#pragma warning disable IDE1006 // Naming Styles
public record TycheData(string type, double run_start, string property, string status, string representation,
    string? status_reason, Dictionary<string, string> arguments, string? how_generated, Dictionary<string, string> features,
    Dictionary<string, string>? coverage, Dictionary<string, string> timing, Dictionary<string, string> metadata);