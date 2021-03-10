// Copyright 2021 Anthony Lloyd
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CsCheck
{
    public class CsCheckException : Exception
    {
        public CsCheckException(string message) : base(message) { }
        public CsCheckException(string message, Exception exception) : base(message, exception) { }
    }

    public class SampleOptions<T>
    {
        public static SampleOptions<T> Default = new();
        public string Seed = Check.Seed;
        public int Size = Check.Size;
        public int Threads = Check.Threads;
        public Func<T, string> Print = t => Check.Print(t);
    }

    public static class Check
    {
        const int MAX_LENGTH = 5000;
        public static int Size = 100;
        public static int Threads = Environment.ProcessorCount;
        public static string Seed;
        public static double Sigma;

        static Check()
        {
            var size = Environment.GetEnvironmentVariable("CsCheck_Size");
            if (!string.IsNullOrWhiteSpace(size)) Size = int.Parse(size);
            var threads = Environment.GetEnvironmentVariable("CsCheck_Threads");
            if (!string.IsNullOrWhiteSpace(threads)) Threads = int.Parse(threads);
            var seed = Environment.GetEnvironmentVariable("CsCheck_Seed");
            if (!string.IsNullOrWhiteSpace(seed)) Seed = PCG.Parse(seed).ToString();
            var sigma = Environment.GetEnvironmentVariable("CsCheck_Sigma");
            if (!string.IsNullOrWhiteSpace(sigma)) Sigma = double.Parse(sigma);
        }

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, SampleOptions<T> options, Action<T> assert)
        {
            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = CsCheck.Size.Max;
            T minT = default;
            Exception minException = null;
            int shrinks = -1;
            if (options.Seed is not null)
            {
                var pcg = PCG.Parse(options.Seed);
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    (t, s) = gen.Generate(pcg);
                    assert(t);
                }
                catch (Exception e)
                {
                    shrinks++;
                    minPCG = pcg;
                    minState = state;
                    minSize = s;
                    minT = t;
                    minException = e;
                }
            }
            var lockObj = new object();
            int skipped = 0;
            Parallel.For(0, options.Seed is null ? options.Size : options.Size - 1,
                new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    (t, s) = gen.Generate(pcg);
                    if (s.IsLessThan(minSize))
                        assert(t);
                    else
                        skipped++;
                }
                catch (Exception e)
                {
                    lock (lockObj)
                    {
                        if (s.IsLessThan(minSize))
                        {
                            shrinks++;
                            minPCG = pcg;
                            minState = state;
                            minSize = s;
                            minT = t;
                            minException = e;
                        }
                    }
                }
            });
            if (minPCG is not null)
            {
                var seedString = minPCG.ToString(minState);
                var tString = options.Print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                throw new CsCheckException(
                    $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {options.Size:#,0} total)\nSample: {tString}"
                    , minException);
            }
        }

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, Action<T> assert, string seed = null, int size = -1, int threads = -1, Func<T, string> print = null)
        {
            var options = new SampleOptions<T>();
            if (seed is not null) options.Seed = seed;
            if (size != -1) options.Size = size;
            if (threads != -1) options.Threads = threads;
            if (print is not null) options.Print = print;
            Sample(gen, options, assert);
        }

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, Action<T> assert) => Sample(gen, SampleOptions<T>.Default, assert);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, SampleOptions<T> options, Func<T, bool> predicate)
        {
            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = CsCheck.Size.Max;
            T minT = default;
            Exception minException = null;
            int shrinks = -1;
            if (options.Seed is not null)
            {
                var pcg = PCG.Parse(options.Seed);
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    (t, s) = gen.Generate(pcg);
                    if (!predicate(t))
                    {
                        shrinks++;
                        minPCG = pcg;
                        minState = state;
                        minSize = s;
                        minT = t;
                    }
                }
                catch (Exception e)
                {
                    shrinks++;
                    minPCG = pcg;
                    minState = state;
                    minSize = s;
                    minT = t;
                    minException = e;
                }
            }

            var lockObj = new object();
            int skipped = 0;
            Parallel.For(0, options.Seed is null ? options.Size : options.Size - 1,
                new ParallelOptions { MaxDegreeOfParallelism = options.Threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    (t, s) = gen.Generate(pcg);
                    if (s.IsLessThan(minSize))
                    {
                        if (!predicate(t))
                        {
                            lock (lockObj)
                            {
                                if (s.IsLessThan(minSize))
                                {
                                    shrinks++;
                                    minPCG = pcg;
                                    minState = state;
                                    minSize = s;
                                    minT = t;
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
                        if (s.IsLessThan(minSize))
                        {
                            shrinks++;
                            minPCG = pcg;
                            minState = state;
                            minSize = s;
                            minT = t;
                            minException = e;
                        }
                    }
                }
            });
            if (minPCG is not null)
            {
                var seedString = minPCG.ToString(minState);
                var tString = options.Print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                throw new CsCheckException(
                    $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {options.Size:#,0} total)\nSample: {tString}"
                    , minException);
            }
        }

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null, int size = -1, int threads = -1, Func<T, string> print = null)
        {
            var options = new SampleOptions<T>();
            if (seed is not null) options.Seed = seed;
            if (size != -1) options.Size = size;
            if (threads != -1) options.Threads = threads;
            if (print is not null) options.Print = print;
            Sample(gen, options, predicate);
        }

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate) => Sample(gen, SampleOptions<T>.Default, predicate);

        /// <summary>Sample the gen once calling the assert.</summary>
        public static void SampleOne<T>(this Gen<T> gen, Action<T> assert, string seed = null, Func<T, string> print = null)
            => Sample(gen, assert, seed, 1, 1, print);

        /// <summary>Sample the gen once calling the predicate.</summary>
        public static void SampleOne<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null, Func<T, string> print = null)
            => Sample(gen, predicate, seed, 1, 1, print);

        /// <summary>Sample model-based operations on a random initial state checking that the actual and model are equal.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, SampleOptions<(Actual, Model)> options,
            Func<Actual, Model, bool> equal, params Gen<Action<Actual, Model>>[] operations)
        {
            initial.Select(Gen.OneOf(operations).Array)
            .Sample(g =>
            {
                var (actual, model) = g.V0;
                foreach (var operation in g.V1)
                    operation(actual, model);
                return equal(actual, model);
            }, options.Seed, options.Size, options.Threads,
            x => "operations: " + x.V1.Length + " " + options.Print(x.V0));
        }

        /// <summary>Sample model-based operations on a random initial state checking that the actual and model are equal.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial,
            Func<Actual, Model, bool> equal, params Gen<Action<Actual, Model>>[] operations)
            => SampleModelBased(initial, SampleOptions<(Actual, Model)>.Default, equal, operations);

        class ConcurrentData<T> { public T State; public uint Stream; public ulong Seed; public (string, Action<T>)[] Operations;
                                  public int Threads; public int[] ThreadIds; public Exception Exception; }

        /// <summary>Sample concurrent operations on a random initial state checking that that result can be linearized.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, SampleOptions<T> options, Func<T, T, bool> equal, params Gen<(string, Action<T>)>[] operations)
        {
            Gen.Create(pcg =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                var (t, size) = initial.Generate(pcg);
                return ((t, stream, seed), size);
            })
            .Select(Gen.OneOf(operations).Array[1, 10].Select(ops => Gen.Int[1, Math.Min(options.Threads, ops.Length)]), (a, b) =>
                new ConcurrentData<T> { State = a.t, Stream = a.stream, Seed = a.seed, Operations = b.V0, Threads = b.V1, ThreadIds = new int[b.V0.Length] }
            )
            .Sample(cd =>
            {
                try
                {
                    ThreadUtils.Run(cd.State, cd.Operations, cd.Threads, cd.ThreadIds);
                }
                catch (Exception e)
                {
                    cd.Exception = e;
                    return false;
                }
                bool linearizable = false;
                Parallel.ForEach(ThreadUtils.Permutations(cd.ThreadIds, cd.Operations), (sequence, state) =>
                {
                    var linearState = initial.Generate(new PCG(cd.Stream, cd.Seed)).Item1;
                    try
                    {
                        ThreadUtils.Run(linearState, sequence, 1);
                        if (equal(cd.State, linearState))
                        {
                            linearizable = true;
                            state.Stop();
                        }
                    }
                    catch { state.Stop(); }
                });
                return linearizable;
            }, options.Seed, options.Size, threads: 1,
            p =>
            {
                var sb = new StringBuilder();
                sb.Append(p.Operations.Length).Append(" operations on ").Append(p.Threads).Append(" threads.");
                sb.Append("\n   Operations: ").Append(Print(p.Operations.Select(i => i.Item1).ToList()));
                sb.Append("\n   On Threads: ").Append(Print(p.ThreadIds));
                sb.Append("\nInitial state: ").Append(options.Print(initial.Generate(new PCG(p.Stream, p.Seed)).Item1));
                sb.Append("\n  Final state: ").Append(p.Exception is not null ? p.Exception.ToString() : options.Print(p.State));
                bool first = true;
                foreach (var sequence in ThreadUtils.Permutations(p.ThreadIds, p.Operations))
                {
                    var linearState = initial.Generate(new PCG(p.Stream, p.Seed)).Item1;
                    string result;
                    try
                    {
                        ThreadUtils.Run(linearState, sequence, 1);
                        result = options.Print(linearState);
                    }
                    catch (Exception e)
                    {
                        result = e.ToString();
                    }
                    sb.Append(first ? "\n   Linearized: " : "\n             : ");
                    sb.Append(Print(sequence.Select(i => i.Item1).ToList()));
                    sb.Append(" -> ");
                    sb.Append(result);
                    first = false;
                }
                return sb.ToString();
            });
        }

        /// <summary>Sample concurrent operations on a random initial state checking that that result can be linearized.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, Func<T, T, bool> equal, params Gen<(string, Action<T>)>[] operations)
            => SampleConcurrent(initial, SampleOptions<T>.Default, equal, operations);

        /// <summary>Assert actual is in line with expected using a chi-squared test to 6 sigma.</summary>
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
            // chi-squared distribution has Mean = k and Variance = 2 k where k is the number of degrees of freedom.
            int k = expected.Length - 1;
            double sigmaSquared = (chi - k) * (chi - k) / k / 2.0;
            if (sigmaSquared > 36.0) throw new CsCheckException(
                "Chi-squared standard deviation = " + Math.Sqrt(sigmaSquared).ToString("0.0"));
        }

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6).</summary>
        public static FasterResult Faster(Action faster, Action slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60_000, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma == 0.0 ? 6.0 : Sigma;
            sigma *= sigma;
            if (threads == -1) threads = Threads;
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
                            for (int i = 1; i < repeat; i++) faster();
                            faster();
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) slower();
                            slower();
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                                if (tf < ts) r.Faster++;
                                else if (tf > ts) r.Slower++;
                            }
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            bool completed = mre.Wait(timeout);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6).</summary>
        public static FasterResult Faster<T>(Func<T> faster, Func<T> slower, Action<T, T> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60_000, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma == 0.0 ? 6.0 : Sigma;
            sigma *= sigma;
            if (threads == -1) threads = Threads;
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
                            for (int i = 1; i < repeat; i++) faster();
                            var vf = faster();
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) slower();
                            var vs = slower();
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            if (assertEqual is null)
                            {
                                if (!vf.Equals(vs))
                                {
                                    exception = new CsCheckException($"Return values differ: faster={vf} slower={vs}");
                                    mre.Set();
                                    return;
                                }
                            }
                            else
                            {
                                try
                                {
                                    assertEqual(vf, vs);
                                }
                                catch (Exception ex)
                                {
                                    exception = ex;
                                    mre.Set();
                                    return;
                                }
                            }
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                                if (tf < ts) r.Faster++;
                                else if (tf > ts) r.Slower++;
                            }
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                });
            bool completed = mre.Wait(timeout);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }
        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sampel of input data.</summary>
        public static FasterResult Faster<T>(this Gen<T> gen, Action<T> faster, Action<T> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60_000, string seed = null, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma == 0.0 ? 6.0 : Sigma;
            sigma *= sigma; // using sigma as sigma squared now
            if (seed is null) seed = Seed;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
                    ulong state = 0;
                    T t = default;
                    try
                    {
                        while (!mre.IsSet)
                        {
                            state = pcg.State;
                            t = gen.Generate(pcg).Item1;
                            var tf = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) faster(t);
                            faster(t);
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) slower(t);
                            slower(t);
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                                if (tf < ts) r.Faster++;
                                else if (tf > ts) r.Slower++;
                            }
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        var tstring = t.ToString();
                        if (tstring.Length > 100) tstring = tstring.Substring(0, 100);
                        exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tstring}", e);
                        mre.Set();
                    }
                });
            var completed = mre.Wait(timeout);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }
        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        public static FasterResult Faster<T1, T2>(this Gen<T1> gen, Func<T1, T2> faster, Func<T1, T2> slower, Action<T2, T2> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60_000, string seed = null, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma == 0.0 ? 6.0 : Sigma;
            sigma *= sigma; // using sigma as sigma squared now
            if (seed is null) seed = Seed;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                Task.Run(() =>
                {
                    var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
                    ulong state = 0;
                    T1 t = default;
                    try
                    {

                        while (!mre.IsSet)
                        {
                            state = pcg.State;
                            t = gen.Generate(pcg).Item1;
                            var tf = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) faster(t);
                            var vf = faster(t);
                            tf = Stopwatch.GetTimestamp() - tf;
                            if (mre.IsSet) return;
                            var ts = Stopwatch.GetTimestamp();
                            for (int i = 1; i < repeat; i++) slower(t);
                            var vs = slower(t);
                            ts = Stopwatch.GetTimestamp() - ts;
                            if (mre.IsSet) return;
                            if (assertEqual is null)
                            {
                                if (!vf.Equals(vs))
                                {
                                    exception = new CsCheckException(
                                        $"Return values differ: CsCheck_Seed = \"{pcg.ToString(state)}\" faster={vf} slower={vs}");
                                    mre.Set();
                                    return;
                                }
                            }
                            else
                            {
                                try
                                {
                                    assertEqual(vf, vs);
                                }
                                catch (Exception ex)
                                {
                                    exception = new CsCheckException(
                                        $"Return values differ: CsCheck_Seed = \"" + pcg.ToString(state) + "\"", ex);
                                    mre.Set();
                                    return;
                                }
                            }
                            var e = (float)(ts - tf) / Math.Max(ts, tf);
                            lock (r)
                            {
                                r.Median.Add(e);
                                if (tf < ts) r.Faster++;
                                else if (tf > ts) r.Slower++;
                            }
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        var tstring = t.ToString();
                        if (tstring.Length > 100) tstring = tstring.Substring(0, 100);
                        exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tstring}", e);
                        mre.Set();
                    }
                });
            var completed = mre.Wait(timeout);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }
        /// <summary>Generate an example that satisfies the predicate.</summary>
        public static T Example<T>(this Gen<T> g, Func<T, bool> predicate, string seed = null, Action<string> output = null)
        {
            if (seed is null)
            {
                var mre = new ManualResetEventSlim();
                T ret = default;
                string message = null;
                var threads = Environment.ProcessorCount;
                while (threads-- > 0)
                    Task.Run(() =>
                    {
                        var pcg = PCG.ThreadPCG;
                        while (true)
                        {
                            if (mre.IsSet) return;
                            var state = pcg.State;
                            var t = g.Generate(pcg).Item1;
                            if (predicate(t))
                            {
                                lock (mre)
                                {
                                    if (message is null)
                                    {
                                        message = $"Example {typeof(T).Name} seed = \"{pcg.ToString(state)}\"";
                                        ret = t;
                                        mre.Set();
                                    }
                                }

                            }
                        }
                    });
                mre.Wait();
                if (output is null) throw new CsCheckException(message); else output(message);
                return ret;
            }
            else
            {
                var pcg = PCG.Parse(seed);
                var t = g.Generate(pcg).Item1;
                if (!predicate(t)) throw new CsCheckException("where clause no longer satisfied");
                return t;
            }
        }

        /// <summary>Check a hash of a series of values. Cache values on a correct run and fail at first difference.</summary>
        public static void Hash(Action<Hash> action, long expected = 0, [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            if (expected == 0)
            {
                var hash = new Hash(null, -1);
                action(hash);
                var offset = hash.BestOffset();
                hash = new Hash(null, offset);
                action(hash);
                var fullHashCode = CsCheck.Hash.FullHash(offset, hash.GetHashCode());
                throw new CsCheckException($"Hash is {fullHashCode}");
            }
            else
            {
                var (offset, expectedHashCode) = CsCheck.Hash.OffsetHash(expected);
                var hash = new Hash(expectedHashCode, offset, memberName, filePath);
                action(hash);
                int actualHashCode = hash.GetHashCode();
                hash.Close();
                if (actualHashCode != expectedHashCode)
                {
                    hash = new Hash(null, -1);
                    action(hash);
                    var offsetCheck = hash.BestOffset();
                    if (offsetCheck != offset)
                    {
                        offset = offsetCheck;
                        hash = new Hash(null, offset);
                        action(hash);
                        actualHashCode = hash.GetHashCode();
                    }
                    var actualFullHash = CsCheck.Hash.FullHash(offset, actualHashCode);
                    throw new CsCheckException($"Actual {actualFullHash} but expected {expected}");
                }
            }
        }

        internal static string Print(object o) => o switch
        {
            string s => s,
            Array { Length: <= 12 } a => "[" + string.Join(", ", a.Cast<object>().Select(Print)) + "]",
            Array a => $"L={a.Length} [{Print(a.GetValue(0))}, {Print(a.GetValue(1))}, {Print(a.GetValue(2))} ... {Print(a.GetValue(a.Length - 2))}, {Print(a.GetValue(a.Length - 1))}]",
            IList { Count: <= 12 } l => "[" + string.Join(", ", l.Cast<object>().Select(Print)) + "]",
            IList l => $"L={l.Count} [{Print(l[0])}, {Print(l[1])}, {Print(l[2])} ... {Print(l[l.Count - 2])}, {Print(l[l.Count - 1])}]",
            IEnumerable<object> e when e.Take(12).Count() <= 12 => "{" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e when e.Take(999).Count() <= 999 => "L=" + e.Count() + " {" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e => "L>999 {" + string.Join(", ", e.Take(6).Select(Print)) + " ... }",
            IEnumerable e => Print(e.Cast<object>()),
            _ => o.ToString(),
        };
    }

    public class FasterResult
    {
        public int Faster;
        public int Slower;
        public MedianEstimator Median;
        internal float SigmaSquared
        {
            // Binomial distribution: Mean = n p, Variance = n p q
            // in this case H0 has n = Faster + Slower, p = 0.5, and q = 0.5
            // sigmas = Abs(Faster - Mean) / Sqrt(Variance)
            //        = Sqrt((Faster - Slower)^2/(Faster + Slower))
            get
            {
                float d = Faster - Slower;
                return d * d / (Faster + Slower);
            }
        }
        public override string ToString()
        {
            var result = $"%[{Median.LowerQuartile * 100.0:#0.0}%..{Median.UpperQuartile * 100.0:#0.0}%]";
            result = Median.Median >= 0.0 ? (Median.Median * 100.0).ToString("#0.0") + result + " faster"
                : (Median.Median * 100.0 / (-1.0 - Median.Median)).ToString("#0.0") + result + " slower";
            return result + $", sigma={Math.Sqrt(SigmaSquared):#0.0} ({Faster:#,0} vs {Slower:#,0})";
        }
        public void Output(Action<string> output)
        {
            output(ToString());
        }
    }
}