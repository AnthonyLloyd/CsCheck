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
        public static void Sample<T>(this Gen<T> gen, Action<T> assert,
            string seed = null, int size = -1, int threads = -1, Func<T, string> print = null)
        {
            if (seed is null) seed = Seed;
            if (size == -1) size = Size;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = CsCheck.Size.Max;
            T minT = default;
            Exception minException = null;
            int shrinks = -1;
            if (seed is not null)
            {
                var pcg = PCG.Parse(seed);
                ulong state = pcg.State;
                Size s = CsCheck.Size.Zero;
                T t = default;
                try
                {
                    assert(t = gen.Generate(pcg, out s));
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
            Parallel.For(0, seed is null ? size : size - 1,
                new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = CsCheck.Size.Zero;
                T t = default;
                try
                {
                    t = gen.Generate(pcg, out s);
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
                var tString = print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                throw new CsCheckException(
                    $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {size:#,0} total)\nSample: {tString}"
                    , minException);
            }
        }

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate,
            string seed = null, int size = -1, int threads = -1, Func<T, string> print = null)
        {
            if (seed is null) seed = Seed;
            if (size == -1) size = Size;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = CsCheck.Size.Max;
            T minT = default;
            Exception minException = null;
            int shrinks = -1;
            if (seed is not null)
            {
                var pcg = PCG.Parse(seed);
                ulong state = pcg.State;
                Size s = CsCheck.Size.Zero;
                T t = default;
                try
                {
                    t = gen.Generate(pcg, out s);
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
            Parallel.For(0, seed is null ? size : size - 1,
                new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
            {
                var pcg = PCG.ThreadPCG;
                ulong state = pcg.State;
                Size s = CsCheck.Size.Zero;
                T t = default;
                try
                {
                    t = gen.Generate(pcg, out s);
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
                var tString = print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                throw new CsCheckException(
                    $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {size:#,0} total)\nSample: {tString}"
                    , minException);
            }
        }

        /// <summary>Sample the gen once calling the assert.</summary>
        public static void SampleOne<T>(this Gen<T> gen, Action<T> assert, string seed = null, Func<T, string> print = null)
            => Sample(gen, assert, seed, 1, 1, print);

        /// <summary>Sample the gen once calling the predicate.</summary>
        public static void SampleOne<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null, Func<T, string> print = null)
            => Sample(gen, predicate, seed, 1, 1, print);

        class ModelBasedData<Actual, Model>
        {
            public Actual ActualState; public Model ModelState; public uint Stream; public ulong Seed;
            public (string, Action<Actual, Model>)[] Operations; public Exception Exception;
        }

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model>[] operations,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
        {
            if (equal is null) equal = ModelEqual;
            if (seed is null) seed = Seed;
            if (size == -1) size = Size;
            if (threads == -1) threads = Threads;
            if (printActual is null) printActual = Print;
            if (printModel is null) printModel = Print;

            var opNameActions = new Gen<(string, Action<Actual, Model>)>[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                var op = operations[i];
                var opName = "Op" + i;
                opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2)) : op;
            }

            Gen.Create((PCG pcg, out Size size) =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                return (initial.Generate(pcg, out size), stream, seed);
            })
            .Select(Gen.OneOf<(string, Action<Actual, Model>)>(opNameActions).Array, (a, b) =>
                 new ModelBasedData<Actual, Model>
                 {
                     ActualState = a.Item1.Item1,
                     ModelState = a.Item1.Item2,
                     Stream = a.stream,
                     Seed = a.seed,
                     Operations = b
                 })
            .Sample(d =>
            {
                try
                {
                    foreach (var operation in d.Operations)
                        operation.Item2(d.ActualState, d.ModelState);
                    return equal(d.ActualState, d.ModelState);
                }
                catch (Exception e)
                {
                    d.Exception = e;
                    return false;
                }
            }, seed, size, threads,
            p =>
            {
                if (p == null) return "";
                var sb = new StringBuilder();
                sb.Append(p.Operations.Length).Append(" operations.");
                sb.Append("\n    Operations: ").Append(Print(p.Operations.Select(i => i.Item1).ToList()));
                var initialState = initial.Generate(new PCG(p.Stream, p.Seed), out _);
                sb.Append("\nInitial Actual: ").Append(printActual(initialState.Item1));
                sb.Append("\nInitial  Model: ").Append(printModel(initialState.Item2));
                if (p.Exception is null)
                {
                    sb.Append("\n  Final Actual: ").Append(printActual(p.ActualState));
                    sb.Append("\n  Final  Model: ").Append(printModel(p.ModelState));
                }
                else
                {
                    sb.Append("\n     Exception: ").Append(p.Exception.ToString());
                }
                return sb.ToString();
            });
        }

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation }, equal, seed, size, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2 }, equal, seed, size, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3 }, equal, seed, size, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4 }, equal, seed, size, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            GenOperation<Actual, Model> operation5,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4, operation5 },
                equal, seed, size, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            GenOperation<Actual, Model> operation5, GenOperation<Actual, Model> operation6,
            Func<Actual, Model, bool> equal = null, string seed = null, int size = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
                equal, seed, size, threads, printActual, printModel);

        class ConcurrentData<T>
        {
            public T State; public uint Stream; public ulong Seed; public (string, Action<T>)[] Operations;
            public int Threads; public int[] ThreadIds; public Exception Exception;
        }

        internal const int MAX_CONCURRENT_OPERATIONS = 10;

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T>[] operations,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
        {
            if (equal is null) equal = Equal;
            if (seed is null) seed = Seed;
            if (size == -1) size = Size;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            var opNameActions = new Gen<(string, Action<T>)>[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                var op = operations[i];
                var opName = "Op" + i;
                opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2)) : op;
            }

            Gen.Create((PCG pcg, out Size size) =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                return (initial.Generate(pcg, out size), stream, seed);
            })
            .Select(Gen.OneOf<(string, Action<T>)>(opNameActions).Array[1, MAX_CONCURRENT_OPERATIONS].Select(ops => Gen.Int[1, Math.Min(threads, ops.Length)]), (a, b) =>
                new ConcurrentData<T> { State = a.Item1, Stream = a.stream, Seed = a.seed, Operations = b.V0, Threads = b.V1 }
            )
            .Sample(cd =>
            {
                try
                {
                    if (replay is null)
                        ThreadUtils.Run(cd.State, cd.Operations, cd.Threads, cd.ThreadIds = new int[cd.Operations.Length]);
                    else
                        ThreadUtils.RunReplay(cd.State, cd.Operations, cd.Threads, cd.ThreadIds = replay);
                }
                catch (Exception e)
                {
                    cd.Exception = e;
                    return false;
                }
                bool linearizable = false;
                Parallel.ForEach(ThreadUtils.Permutations(cd.ThreadIds, cd.Operations), (sequence, state) =>
                {
                    var linearState = initial.Generate(new PCG(cd.Stream, cd.Seed), out _);
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
            }, seed, size, threads: 1,
            p =>
            {
                if (p == null) return "";
                var sb = new StringBuilder();
                sb.Append(p.Operations.Length).Append(" operations on ").Append(p.Threads).Append(" threads.");
                sb.Append("\n   Operations: ").Append(Print(p.Operations.Select(i => i.Item1).ToList()));
                sb.Append("\n   On Threads: ").Append(Print(p.ThreadIds));
                sb.Append("\nInitial state: ").Append(print(initial.Generate(new PCG(p.Stream, p.Seed), out _)));
                sb.Append("\n  Final state: ").Append(p.Exception is not null ? p.Exception.ToString() : print(p.State));
                bool first = true;
                foreach (var sequence in ThreadUtils.Permutations(p.ThreadIds, p.Operations))
                {
                    var linearState = initial.Generate(new PCG(p.Stream, p.Seed), out _);
                    string result;
                    try
                    {
                        ThreadUtils.Run(linearState, sequence, 1);
                        result = print(linearState);
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

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation }, equal, seed, replay, size, threads, print);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation1, operation2 }, equal, seed, replay, size, threads, print);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3 }, equal, seed, replay, size, threads, print);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4 }, equal, seed, replay, size, threads, print);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4, operation5 },
                equal, seed, replay, size, threads, print);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5, GenOperation<T> operation6,
            Func<T, T, bool> equal = null, string seed = null, int[] replay = null, int size = -1, int threads = -1, Func<T, string> print = null)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
                equal, seed, replay, size, threads, print);

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
                            t = gen.Generate(pcg, out _);
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
                            t = gen.Generate(pcg, out _);
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
                            var t = g.Generate(pcg, out _);
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
                var t = g.Generate(pcg, out _);
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

        internal static string Print<T>(T t) => t switch
        {
            string s => s,
            IList { Count: <= 12 } l => "[" + string.Join(", ", l.Cast<object>().Select(Print)) + "]",
            IList l => $"L={l.Count} [{Print(l[0])}, {Print(l[1])}, {Print(l[2])} ... {Print(l[l.Count - 2])}, {Print(l[l.Count - 1])}]",
            IEnumerable<object> e when e.Take(12).Count() <= 12 => "{" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e when e.Take(999).Count() <= 999 => "L=" + e.Count() + " {" + string.Join(", ", e.Select(Print)) + "}",
            IEnumerable<object> e => "L>999 {" + string.Join(", ", e.Take(6).Select(Print)) + " ... }",
            IEnumerable e => Print(e.Cast<object>()),
            _ => t.ToString(),
        };

        internal static bool Equal<T>(T a, T b)
        {
            if (a is IEquatable<T> aie) return aie.Equals(b);
            else if (a is Array aa2 && b is Array ba2 && aa2.Rank == 2)
            {
                if ((aa2.GetLength(0) != ba2.GetLength(0))
                 || (aa2.GetLength(1) != ba2.GetLength(1))) return false;
                for (int i = 0; i < aa2.GetLength(0); i++)
                    for (int j = 0; j < aa2.GetLength(1); j++)
                        if (!aa2.GetValue(i, j).Equals(ba2.GetValue(i, j)))
                            return false;
                return true;
            }
            else if (a is IList ail && b is IList bil)
            {
                if (ail.Count != bil.Count) return false;
                for (int i = 0; i < ail.Count; i++)
                    if (!ail[i].Equals(bil[i]))
                        return false;
                return true;
            }
            else if (a is ICollection aic && b is ICollection bic)
            {
                return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
            }
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        internal static bool ModelEqual<T, M>(T a, M b)
        {
            if (a is IList ail && b is IList bil)
            {
                if (ail.Count != bil.Count) return false;
                for (int i = 0; i < ail.Count; i++)
                    if (!ail[i].Equals(bil[i]))
                        return false;
                return true;
            }
            else if (a is ICollection aic && b is ICollection bic)
            {
                return aic.Count == bic.Count && !aic.Cast<object>().Except(bic.Cast<object>()).Any();
            }
            else if (a is IEnumerable aie && b is IEnumerable bie)
            {
                var aieo = aie.Cast<object>().ToList();
                var bieo = bie.Cast<object>().ToList();
                return aieo.Count == bieo.Count && !aieo.Except(bieo).Any();
            }
            return a.Equals(b);
        }
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