﻿// Copyright 2025 Anthony Lloyd
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

using System.Text;
using System.Diagnostics;

/// <summary>Causal profiling functionality.</summary>
public static class Causal
{
    const int SUMMARY_STAT_COUNT = 6;
    enum RunType { Nothing, CollectTimes, Delay }
    static RunType runType;
    static SpinLock spinLock = new(false);
    static string? delayName;
    static int delayTime, delayCount;
    static long onSince, totalDelay;
    static readonly List<(string, long)> times = [];
    public struct Region { public string Name; public long Start; public long TotalDelay; public long OnSince; }

    /// <summary>Marks the start of causal profiling region.</summary>
    /// <param name="name">The region name.</param>
    public static Region RegionStart(string name)
    {
        if (runType == RunType.Nothing) return new Region();
        var now = Stopwatch.GetTimestamp();
        var lockTaken = false;
        spinLock.Enter(ref lockTaken);
        if (string.Equals(delayName, name) && delayTime < 0 && delayCount++ == 0)
            onSince = now;
        var localTotalDelay = totalDelay;
        var localOnSince = onSince;
        if (lockTaken) spinLock.Exit(false);
        return new Region { Name = name, Start = now, TotalDelay = localTotalDelay, OnSince = localOnSince };
    }

    /// <summary>Marks the end of causal profiling region.</summary>
    /// <param name="region">The region returned from RegionStart.</param>
    public static void RegionEnd(Region region)
    {
        if (runType == RunType.Nothing) return;
        var now = Stopwatch.GetTimestamp();
        var lockTaken = false;
        spinLock.Enter(ref lockTaken);
        if (runType == RunType.CollectTimes)
        {
            times.Add((region.Name, now - region.Start));
            if (lockTaken) spinLock.Exit(false);
        }
        else
        {
            if (delayTime < 0)
            {
                if (string.Equals(region.Name, delayName))
                {
                    if (--delayCount == 0)
                    {
                        totalDelay += (now - onSince) * delayTime / -100L;
                        onSince = 0L;
                    }
                    if (lockTaken) spinLock.Exit(false);
                }
                else
                {
                    var wait = now + totalDelay - region.TotalDelay
                        + ((onSince == 0L ? 0L : now - onSince)
                        + (region.OnSince == 0L ? 0L : region.OnSince - region.Start)) * delayTime / -100L;
                    if (lockTaken) spinLock.Exit(false);
                    while (Stopwatch.GetTimestamp() < wait) { }
                }
            }
            else if (delayTime > 0 && string.Equals(region.Name, delayName))
            {
                if (lockTaken) spinLock.Exit(false);
                var wait = now + ((now - region.Start) * delayTime / 100L);
                while (Stopwatch.GetTimestamp() < wait) { }
            }
            else if (lockTaken)
            {
                spinLock.Exit(false);
            }
        }
    }

    /// <summary>Run causal profiling on a the code in action for a number of iterations or time.</summary>
    /// <param name="action">The code to be causal profiled.</param>
    /// <param name="iter">The number of iteration to collect statistics.</param>
    /// <param name="time">The number of seconds to collect statistics.</param>
    public static Result Profile(Action action, long iter = -1, int time = -1)
    {
        if (iter == -1) iter = Check.Iter;
        if (time == -1) time = Check.Time;
        int Run(RunType run, string? name, int time)
        {
            runType = run;
            delayName = name;
            delayTime = time;
            onSince = 0L;
            delayCount = 0;
            totalDelay = 0L;
            var start = Stopwatch.GetTimestamp();
            action();
            return (int)(Stopwatch.GetTimestamp() - start - totalDelay);
        }
        Run(RunType.CollectTimes, null, 0);
        var summary = times.Select(i => i.Item1).ToHashSet(StringComparer.Ordinal).Select(i => new Result.Row(i)).ToArray();
        times.Clear();
        for (int j = 0; j < SUMMARY_STAT_COUNT; j++)
        {
            var totalTimePct = Run(RunType.CollectTimes, null, 0) * Environment.ProcessorCount * 0.01;
            foreach (var row in summary)
            {
                foreach (var (s, t) in times)
                {
                    if (string.Equals(row.Region, s))
                    {
                        row.Count++;
                        row.Time += t / totalTimePct;
                    }
                }
            }

            times.Clear();
        }
        var delays = new (string?, int, MedianEstimator)[1 + 6 * summary.Length];
        delays[0] = (null, 0, new());
        int i = 1;
        foreach (var row in summary)
        {
            row.Count /= SUMMARY_STAT_COUNT;
            row.Time /= SUMMARY_STAT_COUNT;
            delays[i] = (row.Region, 5, new());
            delays[i + summary.Length] = (row.Region, -5, new());
            delays[i + summary.Length * 2] = (row.Region, 10, new());
            delays[i + summary.Length * 3] = (row.Region, -10, new());
            delays[i + summary.Length * 4] = (row.Region, -15, new());
            delays[i++ + summary.Length * 5] = (row.Region, -20, new());
        }
        if (time < 0)
        {
            for (i = 0; i < iter; i++)
            {
                foreach (var (name, delay, estimator) in delays)
                    estimator.Add(Run(RunType.Delay, name, delay));
            }
        }
        else
        {
            long target = Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
            while (Stopwatch.GetTimestamp() < target)
            {
                foreach (var (name, delay, estimator) in delays)
                    estimator.Add(Run(RunType.Delay, name, delay));
            }
        }
        runType = RunType.Nothing;
        MedianEstimate Estimate(int i) => new(delays[i].Item3);
        var totalPct = Estimate(0) * 0.01;
        for (i = 0; i < summary.Length; i++)
        {
            var row = summary[i];
            row.P5 = 100.0 - Estimate(1 + i) / totalPct;
            row.N5 = 100.0 - Estimate(1 + i + summary.Length) / totalPct;
            row.P10 = 100.0 - Estimate(1 + i + summary.Length * 2) / totalPct;
            row.N10 = 100.0 - Estimate(1 + i + summary.Length * 3) / totalPct;
            row.N15 = 100.0 - Estimate(1 + i + summary.Length * 4) / totalPct;
            row.N20 = 100.0 - Estimate(1 + i + summary.Length * 5) / totalPct;
        }
        return new Result(summary);
    }

    /// <summary>Run causal profiling on a the code in action using gen input data for a number of iterations or time.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="action">The code to be causal profiled.</param>
    /// <param name="iter">The number of iteration to collect statistics.</param>
    /// <param name="time">The number of seconds to collect statistics.</param>
    public static Result Profile<T>(this Gen<T> gen, Action<T> action, long iter = -1, int time = -1)
    {
        var pcg = PCG.ThreadPCG;
        return Profile(() => action(gen.Generate(pcg, null, out _)), iter, time);
    }

    public sealed class Result(Result.Row[] rows)
    {
        public class Row(string region)
        {
            public string Region = region; public int Count; public double Time; public MedianEstimate P10, P5, N5, N10, N15, N20;
        }
        public Row[] Rows = rows;
        public override string ToString()
        {
            var sb = new StringBuilder("\n| Region         |  Count  |  Time%  |    +10%     |     +5%     |     -5%     |    -10%     |    -15%     |    -20%     |\n|:---------------|--------:|--------:|------------:|------------:|------------:|------------:|------------:|------------:|\n");
            foreach (var r in Rows)
            {
                sb.Append("| ");
                sb.Append(r.Region.PadRight(14));
                sb.Append(" | ");
                sb.Append(r.Count.ToString().PadLeft(7));
                sb.Append(" | ");
                sb.Append(r.Time.ToString("0.0").PadLeft(7));
                sb.Append(" | ").Append(r.P10).Append(" | ").Append(r.P5);
                sb.Append(" | ").Append(r.N5).Append(" | ").Append(r.N10).Append(" | ").Append(r.N15).Append(" | ").Append(r.N20);
                sb.Append(" |\n");
            }
            return sb.ToString();
        }
        public void Output(Action<string> output) => output(ToString());
    }
}