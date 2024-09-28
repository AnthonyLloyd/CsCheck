using System.Text.Json;
using System.Threading.Channels;

namespace CsCheck.Logging
{
    public static class GenLogger
    {
        public record LogContext<T>(T Value, bool Success);

        public enum LogProcessor
        {
            Tyche
        }

        public static Func<(Func<Task> loggingTask, Channel<LogContext<T>>)> CreateLogger<T>(StreamWriter w, LogProcessor p, String propertyUnderTest)
        {
            return () =>
            {
                w.AutoFlush = true;
                var channel = Channel.CreateUnbounded<LogContext<T>>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false
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
                                var item = await channel.Reader.ReadAsync().ConfigureAwait(false);
                                var value = item.Value;
                                var timestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeMilliseconds() / 1000.0;
                                var tycheData = new TycheData(
                                    "test_case", timestamp, propertyUnderTest,
                                    item.Success ? "passed" : "failed", JsonSerializer.Serialize(value),
                                    "reason", d, "testing", d, null, d, d
                                );
                                var serializedData = JsonSerializer.Serialize(tycheData);
                                await w.WriteLineAsync(serializedData).ConfigureAwait(false);
                            }
                        };
                        return (t, channel);
                    default:
                        throw new ArgumentOutOfRangeException(nameof(p), p, null);
                }
            };
        }
    }
}

