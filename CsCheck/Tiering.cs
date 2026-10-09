// Copyright 2026 Anthony Lloyd
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace CsCheck;

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;

/// <summary>The JIT tiering settings as the runtime reads them, and a warm-up that waits for tiered compilation to finish.</summary>
internal static class Tiering
{
    const long SettleMs = 30, MaxWarmUpMs = 5_000;
    static readonly bool IsJit = RuntimeFeature.IsDynamicCodeCompiled || JitInfo.GetCompiledMethodCount() > 0;
    static readonly bool IsTiered = IsJit && Flag("TieredCompilation", "System.Runtime.TieredCompilation", true);
    static readonly bool IsQuickJit = IsTiered && Flag("TC_QuickJit", "System.Runtime.TieredCompilation.QuickJit", true);
    static readonly bool IsAggressive = Env("TC_AggressiveTiering") is > 0;
    static readonly uint CallCountThreshold = IsAggressive ? 1
        : Math.Clamp(Env("TC_CallCountThreshold") ?? Knob("System.Runtime.TieredCompilation.CallCountThreshold") ?? 30, 1, ushort.MaxValue);
    static readonly long CallCountingDelayMs = IsAggressive ? 0
        : SingleProcessorDelay(Env("TC_CallCountingDelayMs") ?? Knob("System.Runtime.TieredCompilation.CallCountingDelayMs") ?? 100);
    static readonly string Mode = !IsJit ? "aot" : !IsTiered ? "tiering off" : !IsQuickJit ? "quick jit off" : "tiered";

    // The runtime parses these environment variables as hex.
    static uint? Env(string name)
    {
        var value = (Environment.GetEnvironmentVariable("DOTNET_" + name) ?? Environment.GetEnvironmentVariable("COMPlus_" + name))?.Trim();
        if (value is null) return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    static uint? Knob(string name)
    {
        if (AppContext.GetData(name) is not string value) return null;
        value = value.Trim();
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex) ? hex : 0
            : uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var dec) ? dec : 0;
    }

    static bool Flag(string env, string knob, bool defaultValue)
        => Env(env) is { } value ? value != 0
         : AppContext.GetData(knob) is string s ? string.Equals(s, "true", StringComparison.Ordinal)
         : defaultValue;

    static long SingleProcessorDelay(uint delayMs)
    {
        if (Environment.ProcessorCount != 1) return delayMs;
        var multiplier = Env("TC_DelaySingleProcMultiplier") ?? 10;
        var delay = (ulong)delayMs * multiplier;
        return multiplier > 1 && delay <= uint.MaxValue ? (long)delay : delayMs;
    }

    /// <summary>Describes the JIT the measured code ran under, warning when it may have been unoptimised tier 0 code.</summary>
    public static string Describe(long? warmUpTicks)
    {
        if (!IsTiered || warmUpTicks is null && !IsQuickJit) return Mode;
        if (warmUpTicks is null) return $"{Mode} - MAY BE TIER 0, USE warmUp OR <TieredCompilation>false</TieredCompilation>";
        var ms = Math.Abs(warmUpTicks.Value) * 1000 / Stopwatch.Frequency;
        return warmUpTicks >= 0 ? $"{Mode}, warmed up in {ms}ms"
             : IsQuickJit ? $"{Mode}, warm up timed out after {ms}ms - MAY BE TIER 0"
             : $"{Mode}, warm up timed out after {ms}ms";
    }

    /// <summary>Calls pair until the JIT has stopped compiling it, for at most 5 seconds. Returns the ticks taken, negative if it timed out.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static long WarmUp(Action pair, int repeat)
    {
        var settler = new Settler(repeat);
        do pair(); while (!settler.Done());
        return settler.Elapsed;
    }

    /// <inheritdoc cref="WarmUp(Action, int)"/>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static async Task<long> WarmUpAsync(Func<Task> pair, int repeat)
    {
        var settler = new Settler(repeat);
        do await pair().ConfigureAwait(false); while (!settler.Done());
        return settler.Elapsed;
    }

    // One listener for the process, never disposed: disposing EventListeners concurrently can deadlock the runtime (dotnet/runtime#96219).
    static readonly Lazy<Listener?> SharedListener = new(Listener.Create);

    sealed class Settler(int repeat)
    {
        readonly long start = Stopwatch.GetTimestamp();
        readonly int pairsNeeded = IsTiered ? (int)((CallCountThreshold + repeat - 1) / repeat) + 1 : 2;
        readonly Listener? listener = IsTiered ? SharedListener.Value : null;
        long jitCount = JitInfo.GetCompiledMethodCount(), lastChange = Stopwatch.GetTimestamp(), ready;
        int events, pairs;
        public long Elapsed;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public bool Done()
        {
            var now = Stopwatch.GetTimestamp();
            if (!IsTiered)
            {
                Elapsed = now - start;
                return ++pairs >= pairsNeeded;
            }
            var count = JitInfo.GetCompiledMethodCount();
            var tieringEvents = listener?.Events ?? 0;
            if (count != jitCount || tieringEvents != events)
            {
                jitCount = count;
                events = tieringEvents;
                lastChange = now;
                pairs = 0;
                ready = 0;
            }
            else if (++pairs == pairsNeeded)
            {
                ready = now;
            }
            if (now - start > MaxWarmUpMs * Stopwatch.Frequency / 1000)
            {
                Elapsed = start - now;
                return true;
            }
            if (ready == 0 || now - ready < SettleMs * Stopwatch.Frequency / 1000 || listener is { Paused: true } or { Busy: true })
                return false;
            // Nothing reports an active tiering delay except the Pause event sent when it starts, and it ends up to two delays after the last first call.
            if (CallCountingDelayMs > 0 && listener is not { Resumed: true } && now - lastChange < (2 * CallCountingDelayMs + SettleMs) * Stopwatch.Frequency / 1000)
                return false;
            Elapsed = now - start;
            return true;
        }
    }

    sealed class Listener : EventListener
    {
        const EventKeywords CompilationKeyword = (EventKeywords)0x1000000000;
        const int Pause = 281, Resume = 282, BackgroundJitStart = 283, BackgroundJitStop = 284;
        EventSource? runtime;
        volatile bool paused, resumed, busy;
        int events;
        public bool Paused => paused;
        public bool Resumed => resumed;
        public bool Busy => busy;
        public int Events => Volatile.Read(ref events);

        public static Listener? Create()
        {
            var listener = new Listener();
            if (listener.runtime is not null)
            {
                listener.EnableEvents(listener.runtime, EventLevel.Informational, CompilationKeyword);
                if (listener.runtime.IsEnabled(EventLevel.Informational, CompilationKeyword)) return listener;
            }
            listener.Dispose();
            return null;
        }

        // The base constructor calls this before the listener is ready, so only capture the source.
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, "Microsoft-Windows-DotNETRuntime", StringComparison.Ordinal)) runtime = eventSource;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            switch (eventData.EventId)
            {
                case Pause: paused = true; break;
                case Resume: paused = false; resumed = true; break;
                case BackgroundJitStart: busy = true; break;
                case BackgroundJitStop: busy = eventData.Payload is [_, uint pending, ..] && pending != 0; break;
                default: return;
            }
            Interlocked.Increment(ref events);
        }
    }
}
