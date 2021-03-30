using System;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace CsCheck
{
    public static class Causal
    {
        const int SUMMARY_STAT_COUNT = 6;
        enum RunType { Nothing, CollectTimes, Delay }
        static RunType runType;
        static SpinLock spinLock = new(false);
        static string delayName;
        static int delayTime, delayCount;
        static long onSince, totalDelay;
        static readonly List<(string, long)> times = new();
        public struct Region { public string Name; public long Start; public long TotalDelay; public long OnSince; }

        public static Region RegionStart(string name)
        {
            if (runType == RunType.Nothing) return new Region();
            var now = Stopwatch.GetTimestamp();
            var lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (delayName == name && delayTime < 0)
                if (delayCount++ == 0)
                    onSince = now;
            var localTotalDelay = totalDelay;
            var localOnSince = onSince;
            if (lockTaken) spinLock.Exit(false);
            return new Region { Name = name, Start = now, TotalDelay = localTotalDelay, OnSince = localOnSince };
        }

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
                    if (region.Name == delayName)
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
                        while (Stopwatch.GetTimestamp() < wait) { };
                    }
                }
                else if (delayTime > 0 && region.Name == delayName)
                {
                    if (lockTaken) spinLock.Exit(false);
                    var wait = now + (now - region.Start) * delayTime / 100L;
                    while (Stopwatch.GetTimestamp() < wait) { };
                }
                else if (lockTaken) spinLock.Exit(false);
            }
        }

        public static Result Profile(Action action, long iter = -1, int time = -1)
        {
            if (iter == -1) iter = Check.Iter;
            if (time == -1) time = Check.Time;
            int Run(RunType run, string name, int time)
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
            var summary = times.Select(i => i.Item1).Distinct().Select(i => new Result.Row { Region = i }).ToArray();
            times.Clear();
            for (int j = 0; j < SUMMARY_STAT_COUNT; j++)
            {
                var totalTimePct = Run(RunType.CollectTimes, null, 0) * Environment.ProcessorCount * 0.01;
                foreach (var row in summary)
                    foreach (var (s, t) in times)
                        if (row.Region == s)
                        {
                            row.Count++;
                            row.Time += t / totalTimePct;
                        }
                times.Clear();
            }
            var delays = new (string, int, MedianEstimator)[1 + 6 * summary.Length];
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
                    foreach (var (name, delay, estimator) in delays)
                        estimator.Add(Run(RunType.Delay, name, delay));
            }
            else
            {
                long target = Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
                while (Stopwatch.GetTimestamp() < target)
                    foreach (var (name, delay, estimator) in delays)
                        estimator.Add(Run(RunType.Delay, name, delay));
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
            return new Result { Rows = summary };
        }

        public static Result Profile<T>(this Gen<T> gen, Action<T> action, long iter = -1, int time = -1)
        {
            var pcg = PCG.ThreadPCG;
            return Profile(() => action(gen.Generate(pcg, null, out _)), iter, time);
        }

        public class Result
        {
            public class Row { public string Region; public int Count; public double Time; public MedianEstimate P10, P5, N5, N10, N15, N20; }
            public Row[] Rows;
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
}