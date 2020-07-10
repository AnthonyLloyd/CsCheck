using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CsCheck
{
    public class CsCheckException : Exception
    {
        public CsCheckException(string message) : base(message) { }
        public CsCheckException(string message, Exception exception) : base(message, exception) { }
    }

    public static class Check
    {
        public readonly static int SampleSize = 100;
        public readonly static string SampleSeed;
        public readonly static double FasterSigma;
        public readonly static string FasterSeed;
        static Check()
        {
            var sampleSize = Environment.GetEnvironmentVariable("CsCheck_SampleSize");
            if (!string.IsNullOrWhiteSpace(sampleSize)) SampleSize = int.Parse(sampleSize);
            var sampleSeed = Environment.GetEnvironmentVariable("CsCheck_SampleSeed");
            if (!string.IsNullOrWhiteSpace(sampleSeed)) SampleSeed = PCG.Parse(sampleSeed).ToString();
            var fasterSigma = Environment.GetEnvironmentVariable("CsCheck_FasterSigma");
            if (!string.IsNullOrWhiteSpace(fasterSigma)) FasterSigma = double.Parse(fasterSigma);
            var fasterSeed = Environment.GetEnvironmentVariable("CsCheck_FasterSeed");
            if (!string.IsNullOrWhiteSpace(fasterSeed)) FasterSeed = PCG.Parse(fasterSeed).ToString();
        }
        public static void Sample<T>(this Gen<T> gen, Action<T> action, string seed = null, int size = -1, int threads = -1)
        {
            if (size == -1) size = SampleSize;
            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = null;
            Exception minException = null;

            if(seed != null || SampleSeed != null)
            {
                var pcg = PCG.Parse(seed ?? SampleSeed);
                ulong state = pcg.State;
                Size s = null;
                try
                {
                    var t = gen.Generate(pcg);
                    s = t.Item2;
                    action(t.Item1);
                }
                catch (Exception e)
                {
                    minPCG = pcg;
                    minState = state;
                    minSize = s;
                    minException = e;
                }
            }

            var lockObj = new object();
            int shrinks = 0, skipped = 0;
            Parallel.For(0, size, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = null;
                try
                {
                    var t = gen.Generate(pcg);
                    s = t.Item2;
                    if (minSize is null || s.IsLessThan(minSize))
                        action(t.Item1);
                    else
                        skipped++;
                }
                catch (Exception e)
                {
                    lock (lockObj)
                    {
                        if (minSize is null || s.IsLessThan(minSize))
                        {
                            if(minSize is object) shrinks++;
                            minPCG = pcg;
                            minState = state;
                            minSize = s;
                            minException = e;
                        }
                    }
                }
            });

            if (minPCG != null) throw new CsCheckException(
                $"$env:CsCheck_SampleSeed = '{minPCG.ToString(minState)}' ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {size:#,0} total)"
                    , minException);
        }

        public static void SampleOne<T>(this Gen<T> gen, Action<T> action, string seed = null)
        {
            Sample(gen, action, seed, 1, 1);
        }

        public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null, int size = -1, int threads = -1)
        {
            if (size == -1) size = SampleSize;
            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = null;
            Exception minException = null;

            if (seed != null || SampleSeed != null)
            {
                var pcg = PCG.Parse(seed ?? SampleSeed);
                ulong state = pcg.State;
                Size s = null;
                try
                {
                    var t = gen.Generate(pcg);
                    s = t.Item2;
                    if(!predicate(t.Item1))
                    {
                        minPCG = pcg;
                        minState = state;
                        minSize = s;
                    }
                }
                catch (Exception e)
                {
                    minPCG = pcg;
                    minState = state;
                    minSize = s;
                    minException = e;
                }
            }

            var lockObj = new object();
            int shrinks = 0, skipped = 0;
            Parallel.For(0, size, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = null;
                try
                {
                    var t = gen.Generate(pcg);
                    s = t.Item2;
                    if (minSize is null || s.IsLessThan(minSize))
                    {
                        if (!predicate(t.Item1))
                        {
                            lock (lockObj)
                            {
                                if (minSize is null || s.IsLessThan(minSize))
                                {
                                    if (minSize is object) shrinks++;
                                    minPCG = pcg;
                                    minState = state;
                                    minSize = s;
                                }
                            }
                        }
                    }
                    else skipped++;
                }
                catch (Exception e)
                {
                    lock (lockObj)
                    {
                        if (minSize is null || s.IsLessThan(minSize))
                        {
                            if (minSize is object) shrinks++;
                            minPCG = pcg;
                            minState = state;
                            minSize = s;
                            minException = e;
                        }
                    }
                }
            });

            if (minPCG != null) throw new CsCheckException(
                $"$env:CsCheck_SampleSeed = '{minPCG.ToString(minState)}' ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {size:#,0} total)"
                    , minException);
        }

        public static void SampleOne<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null)
        {
            Sample(gen, predicate, seed, 1, 1);
        }

        public static void ChiSquared(int[] expected, int[] actual)
        {
            if (expected.Length != actual.Length) throw new CsCheckException("Expected and actual lengths need to be the same.");
            if (Array.Exists(expected, e => e <= 5)) throw new CsCheckException("Expected frequency for all buckets needs to be above 5.");
            double chi = 0.0;
            for (int i = 0; i < expected.Length; i++)
            {
                double e = expected[i];
                double d = actual[i] - e;
                chi += d * d / e;
            }
            double mean = expected.Length - 1;
            double sdev = Math.Sqrt(2 * mean);
            double SDs = (chi - mean) / sdev;
            if (Math.Abs(SDs) > 6.0) throw new CsCheckException("Chi-squared standard deviation = " + SDs.ToString("0.0"));
        }

        public static FasterResult Faster(Action faster, Action slower, double sigma = -1.0, int threads = -1, int timeout = -1)
        {
            if (sigma == -1.0) sigma = FasterSigma == 0.0 ? 6.0 : FasterSigma;
            sigma *= sigma;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    try
                    {
                        while (!mre.IsSet)
                        {
                            var tf = Stopwatch.GetTimestamp();
                            faster();
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            slower();
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                            }
                            if (tf < ts) Interlocked.Increment(ref r.Faster);
                            else if (tf > ts) Interlocked.Increment(ref r.Slower);
                            if (r.Variance >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            mre.Wait(timeout);
            if (exception != null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            return r;
        }

        public static FasterResult Faster<T>(Func<T> faster, Func<T> slower, double sigma = -1.0, int threads = -1, int timeout = -1)
        {
            if (sigma == -1.0) sigma = FasterSigma == 0.0 ? 6.0 : FasterSigma;
            sigma *= sigma;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    try
                    {
                        while (!mre.IsSet)
                        {
                            var tf = Stopwatch.GetTimestamp();
                            var vf = faster();
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            var vs = slower();
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            if (!vf.Equals(vs))
                            {
                                exception = new CsCheckException($"Return values differ: faster={vf} slower={vs}");
                                mre.Set();
                                return;
                            }
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                            }
                            if (tf < ts) Interlocked.Increment(ref r.Faster);
                            else if (tf > ts) Interlocked.Increment(ref r.Slower);
                            if (r.Variance >= sigma) mre.Set();
                        }
                    }
                    catch(Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            mre.Wait(timeout);
            if (exception != null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            return r;
        }

        public static FasterResult Faster<T>(this Gen<T> gen, Action<T> faster, Action<T> slower,
            double sigma = -1.0, int threads = -1, int timeout = -1)
        {
            if (sigma == -1.0) sigma = FasterSigma == 0.0 ? 6.0 : FasterSigma;
            sigma *= sigma;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    try
                    {
                        var pcg = PCG.ThreadPCG;
                        while (!mre.IsSet)
                        {
                            var t = gen.Generate(pcg).Item1;
                            var tf = Stopwatch.GetTimestamp();
                            faster(t);
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            slower(t);
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                            }
                            if (tf < ts) Interlocked.Increment(ref r.Faster);
                            else if (tf > ts) Interlocked.Increment(ref r.Slower);
                            if (r.Variance >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            mre.Wait(timeout);
            if (exception != null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            return r;
        }

        public static FasterResult Faster<T1, T2>(this Gen<T1> gen, Func<T1, T2> faster, Func<T1, T2> slower,
            double sigma = -1.0, int threads = -1, int timeout = -1, string seed = null)
        {
            if (sigma == -1.0) sigma = FasterSigma == 0.0 ? 6.0 : FasterSigma;
            sigma *= sigma;
            if (seed == null) seed = SampleSeed;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    try
                    {
                        var pcg = seed == null ? PCG.ThreadPCG : PCG.Parse(seed);
                        while (!mre.IsSet)
                        {
                            var state = pcg.State;
                            var t = gen.Generate(pcg).Item1;
                            var tf = Stopwatch.GetTimestamp();
                            var vf = faster(t);
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            var vs = slower(t);
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            if (!vf.Equals(vs))
                            {
                                exception = new CsCheckException(
                                    $"Return values differ: CsCheck_FasterSeed={pcg.ToString(state)} faster={vf} slower={vs}");
                                mre.Set();
                                return;
                            }
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                            }
                            if (tf < ts) Interlocked.Increment(ref r.Faster);
                            else if (tf > ts) Interlocked.Increment(ref r.Slower);
                            if (r.Variance >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            mre.Wait(timeout);
            if (exception != null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            return r;
        }
    }

    public class FasterResult
    {
        public int Faster;
        public int Slower;
        public MedianEstimator Median;
        internal float Variance
        {
            get
            {
                float d = Faster - Slower;
                d *= d;
                return d / (Faster + Slower);
            }
        }
        public override string ToString()
        {
            var result = $"%[-{Median.MADless * 100.0:#0}..+{Median.MADmore * 100.0:#0}]";
            result = Median.Median >= 0.0 ? (Median.Median * 100.0).ToString("#0.0") + result + " faster"
                : (Median.Median * 100.0 / (-1.0 - Median.Median)).ToString("#0.0") + result + " slower";
            return result + $", sigma={Math.Sqrt(Variance):#0.0} ({Faster:#,0} vs {Slower:#,0})";
        }
    }

    public class MedianEstimator
    {
        int N, n2 = 2, n3 = 3, n4 = 4;
        double q1, q2, q3, q4, q5;
        internal double Median => q3;
        internal double MADless => q3 - q2;
        internal double MADmore => q4 - q3;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Adjust(double p, int n1, ref int n2, int n3, double q1, ref double q2, double q3)
        {
            double d = N * p - n2;
            if ((d >= 1.0 && n3 - n2 > 1) || (d <= -1.0 && n1 - n2 < -1))
            {
                int ds = Math.Sign(d);
                double q = q2 + (double)ds / (n3 - n1) * ((n2 - n1 + ds) * (q3 - q2) / (n3 - n2) + (n3 - n2 - ds) * (q2 - q1) / (n2 - n1));
                q = q1 < q && q < q3 ? q :
                    ds == 1 ? q2 + (q3 - q2) / (n3 - n2) :
                    q2 - (q1 - q2) / (n1 - n2);
                n2 += ds;
                q2 = q;
            }
        }
        internal void Add(float s)
        {
            N++;
            if (N > 5)
            {
                if (s < q1) q1 = s;
                if (s < q2) n2++;
                if (s < q3) n3++;
                if (s < q4) n4++;
                if (s > q5) q5 = s;
                Adjust(0.25, 1, ref n2, n3, q1, ref q2, q3);
                Adjust(0.50, n2, ref n3, n4, q2, ref q3, q4);
                Adjust(0.75, n2, ref n4, N, q3, ref q4, q5);
            }
            else if (N == 5)
            {
                var a = new[] { q1, q2, q3, q4, s };
                Array.Sort(a);
                q1 = a[0];
                q2 = a[1];
                q3 = a[2];
                q4 = a[3];
                q5 = a[4];
            }
            else if (N == 4) q4 = s;
            else if (N == 3) q3 = s;
            else if (N == 2) q2 = s;
            else q1 = s;
        }
    }
}