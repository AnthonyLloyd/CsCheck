using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace CsCheck
{
    public static class Causal
    {
        public struct Region { public string Name; public long Start; public long TotalDelay; public long OnSince; }
        enum RunType { Nothing, CollectTimes, Delay }
        static RunType runType;
        static SpinLock spinLock = new(false);
        static string delayName;
        static int delayTime, delayCount;
        static long onSince, totalDelay;
        static readonly List<(string, long)> times = new();

        public static Region RegionStart(string name)
        {
            if (runType == RunType.Nothing) return new Region();
            var now = Stopwatch.GetTimestamp();
            var lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (delayName == name && delayTime < 0)
            {
                if (delayCount++ == 0)
                {
                    if (onSince != 0L) throw new Exception($"um {onSince}"); // remove me
                    onSince = now;
                }
            }
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
                else
                {
                    if (lockTaken) spinLock.Exit(false);
                }
            }
        }

        public static CausalResult Profile(int n, Action action)
        {
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
            times.Clear();
            List<CausalResultRow> Times()
            {
                var totalTimePct = Run(RunType.CollectTimes, null, 0) * Environment.ProcessorCount * 0.01;
                var summaries = times.GroupBy(t => t.Item1, (r, s) =>
                    new CausalResultRow { Region = r, Count = s.Count(), Time = s.Sum(i => i.Item2) / totalTimePct })
                    .ToList();
                times.Clear();
                return summaries;
            }

            var summary =
                Enumerable.Range(0, 6).SelectMany(_ => Times())
                .GroupBy(i => i.Region, (r, s) =>
                    new CausalResultRow { Region = r, Count = s.Sum(i => i.Count) / s.Count(), Time = Median(s.Select(i => i.Time)) })
                .ToDictionary(i => i.Region);

            var delays = new (string, int, MedianEstimator)[1 + 6 * summary.Count];
            delays[0] = (null, 0, new());
            int i = 1;
            foreach (var r in summary.Keys) delays[i++] = (r, 5, new());
            foreach (var r in summary.Keys) delays[i++] = (r, -5, new());
            foreach (var r in summary.Keys) delays[i++] = (r, 10, new());
            foreach (var r in summary.Keys) delays[i++] = (r, -10, new());
            foreach (var r in summary.Keys) delays[i++] = (r, -15, new());
            foreach (var r in summary.Keys) delays[i++] = (r, -20, new());

            for (i = 1; i <= n; i++)
                for (int j = 0; j < delays.Length; j++)
                {
                    var (name, time, estimator) = delays[j];
                    estimator.Add(Run(RunType.Delay, name, time));
                }

            runType = RunType.Nothing;

            static MedianEstimate Estimate(MedianEstimator e) => new() { Median = e.Median, Error = e.UpperQuartile - e.LowerQuartile };
            var totalPct = Estimate(delays[0].Item3) * 0.01;
            return new CausalResult
            {
                Rows = summary.Values
                .Select(s =>
                {
                    var r = new CausalResultRow
                    {
                        Region = s.Region,
                        Count = s.Count,
                        Time = s.Time,
                        P10 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == 10).Item3) / totalPct,
                        P5 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == 5).Item3) / totalPct,
                        N5 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == -5).Item3) / totalPct,
                        N10 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == -10).Item3) / totalPct,
                        N15 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == -15).Item3) / totalPct,
                        N20 = 100.0 - Estimate(delays.First(i => i.Item1 == s.Region && i.Item2 == -20).Item3) / totalPct,
                    };
                    return r;
                })
                .ToArray()
            };
        }

        static double Median(IEnumerable<double> s)
        {
            var a = s.ToArray();
            Array.Sort(a);
            return a.Length % 2 == 0 ? (a[a.Length / 2] + a[a.Length / 2 - 1]) * 0.5 : a[a.Length / 2];
        }

        public class CausalResultRow { public string Region; public int Count; public double Time; public MedianEstimate P10, P5, N5, N10, N15, N20; }

        public class CausalResult
        {
            public CausalResultRow[] Rows;
            public override string ToString()
            {
                var sb = new StringBuilder("| Region         |  Count  |  Time%  |     +10%     |      +5%     |      -5%     |     -10%     |     -15%     |     -20%     |\n|:---------------|--------:|--------:|-------------:|-------------:|-------------:|-------------:|-------------:|-------------:|      \n");
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
                    sb.Append("\n");
                }
                return sb.ToString();
            }
            public void Output(Action<string> output) => output(ToString());
        }

        public struct MedianEstimate
        {
            public double Median;
            public double Error;
            static double Sqr(double x) => x * x;
            public static MedianEstimate operator -(double a, MedianEstimate e) => new() { Median = a - e.Median, Error = e.Error };
            public static MedianEstimate operator -(MedianEstimate e, double a) => new() { Median = e.Median - a, Error = e.Error };
            public static MedianEstimate operator -(MedianEstimate a, MedianEstimate b) =>
                new() { Median = a.Median - b.Median, Error = Math.Sqrt(Sqr(a.Error) + Sqr(b.Error)) };
            public static MedianEstimate operator *(MedianEstimate e, double a) => new() { Median = e.Median * a, Error = e.Error * a };
            public static MedianEstimate operator /(MedianEstimate a, MedianEstimate b) =>
                new() { Median = a.Median / b.Median, Error = Math.Sqrt(Sqr(a.Error / a.Median) * Sqr(b.Error / b.Median)) * Math.Abs(a.Median / b.Median) };
            public override string ToString() =>
                Math.Min(Math.Max(Median, -99.9), 99.9).ToString("0.0").PadLeft(5) + " ±" + Math.Min(Error, 99.9).ToString("0.0").PadLeft(4);
        }
    }
}