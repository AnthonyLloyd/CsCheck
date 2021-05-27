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
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.IO;

namespace CsCheck
{
    public class CsCheckException : Exception
    {
        public CsCheckException(string message) : base(message) { }
        public CsCheckException(string message, Exception exception) : base(message, exception) { }
    }

    /// <summary>Main random testing Check functions.</summary>
    public static partial class Check
    {
        const int MAX_LENGTH = 5000;
        /// <summary>The number of iterations to run in the sample (default 100).</summary>
        public static long Iter = 100;
        /// <summary>The number of seconds to run the sample.</summary>
        public static int Time = -1;
        /// <summary>The number of times to retry the seed to reproduce a SampleConcurrent fail (default 100).</summary>
        public static int Replay = 100;
        /// <summary>The number of threads to run the sample on (default number logical CPUs).</summary>
        public static int Threads = Environment.ProcessorCount;
        /// <summary>The initial seed to use for the first iteration.</summary>
        public static string Seed;
        /// <summary>The sigma to use for Faster (default 6).</summary>
        public static double Sigma = 6.0;

        static Check()
        {
            var iter = Environment.GetEnvironmentVariable("CsCheck_Iter");
            if (!string.IsNullOrWhiteSpace(iter)) Iter = long.Parse(iter);
            var time = Environment.GetEnvironmentVariable("CsCheck_Time");
            if (!string.IsNullOrWhiteSpace(time)) Time = int.Parse(time);
            var replay = Environment.GetEnvironmentVariable("CsCheck_Replay");
            if (!string.IsNullOrWhiteSpace(replay)) Replay = int.Parse(replay);
            var threads = Environment.GetEnvironmentVariable("CsCheck_Threads");
            if (!string.IsNullOrWhiteSpace(threads)) Threads = int.Parse(threads);
            var seed = Environment.GetEnvironmentVariable("CsCheck_Seed");
            if (!string.IsNullOrWhiteSpace(seed)) Seed = PCG.Parse(seed).ToString();
            var sigma = Environment.GetEnvironmentVariable("CsCheck_Sigma");
            if (!string.IsNullOrWhiteSpace(sigma)) Sigma = double.Parse(sigma);
        }

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T>(this Gen<T> gen, Action<T> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null)
        {
            if (seed is null) seed = Seed;
            if (iter == -1) iter = Iter;
            if (time == -1) time = Time;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = null;
            T minT = default;
            Exception minException = null;

            int shrinks = -1;
            if (seed is not null)
            {
                var pcg = PCG.Parse(seed);
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    assert(t = gen.Generate(pcg, null, out s));
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
            int skipped = 0;
            bool isIter = time < 0;
            long target = isIter ? seed is null ? iter : iter - 1
                        : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
            long total = seed is null ? 0 : 1;
            var cde = new CountdownEvent(threads);
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    var pcg = PCG.ThreadPCG;
                    Size s = null;
                    T t = default;
                    while ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) >= 0)
                    {
                        ulong state = pcg.State;
                        try
                        {
                            t = gen.Generate(pcg, minSize, out s);
                            if (Size.IsLessThan(s, minSize))
                                assert(t);
                            else
                                skipped++;
                        }
                        catch (Exception e)
                        {
                            lock (cde)
                            {
                                if (Size.IsLessThan(s, minSize))
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
                        Interlocked.Increment(ref total);
                    }
                    cde.Signal();
                }, null);
            cde.Wait();
            if (minPCG is not null)
            {
                var seedString = minPCG.ToString(minState);
                var tString = print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                var summary = $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {total:#,0} total).\n";
                throw new CsCheckException(summary + tString, minException);
            }
        }

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Action<T1, T2, T3, T4, T5, T6> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Action<T1, T2, T3, T4, T5, T6, T7> assert,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string> print = null)
            => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null)
        {
            if (seed is null) seed = Seed;
            if (iter == -1) iter = Iter;
            if (time == -1) time = Time;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            PCG minPCG = null;
            ulong minState = 0UL;
            Size minSize = null;
            T minT = default;
            Exception minException = null;

            int shrinks = -1;
            if (seed is not null)
            {
                var pcg = PCG.Parse(seed);
                ulong state = pcg.State;
                Size s = null;
                T t = default;
                try
                {
                    t = gen.Generate(pcg, null, out s);
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
            int skipped = 0;
            bool isIter = time < 0;
            long target = isIter ? seed is null ? iter : iter - 1
                        : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
            long total = seed is null ? 0 : 1;
            var cde = new CountdownEvent(threads);
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    var pcg = PCG.ThreadPCG;
                    Size s = null;
                    T t = default;
                    while ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) >= 0)
                    {
                        ulong state = pcg.State;
                        try
                        {
                            t = gen.Generate(pcg, minSize, out s);
                            if (Size.IsLessThan(s, minSize))
                            {
                                if (!predicate(t))
                                {
                                    lock (cde)
                                    {
                                        if (Size.IsLessThan(s, minSize))
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
                            lock (cde)
                            {
                                if (Size.IsLessThan(s, minSize))
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
                        Interlocked.Increment(ref total);
                    }
                    cde.Signal();
                }, null);
            cde.Wait();
            if (minPCG is not null)
            {
                var seedString = minPCG.ToString(minState);
                var tString = print(minT);
                if (tString.Length > MAX_LENGTH) tString = tString.Substring(0, MAX_LENGTH) + " ...";
                var summary = $"Set seed: \"{seedString}\" or $env:CsCheck_Seed = \"{seedString}\" to reproduce ({shrinks:#,0} shrinks, {skipped:#,0} skipped, {total:#,0} total).\n";
                throw new CsCheckException(summary + tString, minException);
            }
        }

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), seed, iter, time, threads, print);

        /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, bool> predicate,
            string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string> print = null)
            => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

        /// <summary>Sample the gen once calling the assert.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void SampleOne<T>(this Gen<T> gen, Action<T> assert, string seed = null, Func<T, string> print = null)
            => Sample(gen, assert, seed, 1, -2, 1, print);

        /// <summary>Sample the gen once calling the predicate.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void SampleOne<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null, Func<T, string> print = null)
            => Sample(gen, predicate, seed, 1, -2, 1, print);

        class ModelBasedData<Actual, Model>
        {
            public Actual ActualState; public Model ModelState; public uint Stream; public ulong Seed;
            public (string, Action<Actual, Model>)[] Operations; public Exception Exception;
        }

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operations">The operation generators that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model>[] operations,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
        {
            if (equal is null) equal = ModelEqual;
            if (seed is null) seed = Seed;
            if (iter == -1) iter = Iter;
            if (time == -1) time = Time;
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

            Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                return (initial.Generate(pcg, null, out size), stream, seed);
            })
            .Select(Gen.OneOf(opNameActions).Array, (a, b) =>
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
            }, seed, iter, time, threads,
            p =>
            {
                if (p == null) return "";
                var sb = new StringBuilder();
                sb.Append("\n    Operations: ").Append(Print(p.Operations.Select(i => i.Item1).ToList()));
                var initialState = initial.Generate(new PCG(p.Stream, p.Seed), null, out _);
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
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation">The operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation }, equal, seed, iter, time, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state.</param>
        /// <param name="operation2">An operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2 }, equal, seed, iter, time, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state.</param>
        /// <param name="operation2">An operation generator that can act on the state.</param>
        /// <param name="operation3">An operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3 }, equal, seed, iter, time, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state.</param>
        /// <param name="operation2">An operation generator that can act on the state.</param>
        /// <param name="operation3">An operation generator that can act on the state.</param>
        /// <param name="operation4">An operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4 }, equal, seed, iter, time, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state.</param>
        /// <param name="operation2">An operation generator that can act on the state.</param>
        /// <param name="operation3">An operation generator that can act on the state.</param>
        /// <param name="operation4">An operation generator that can act on the state.</param>
        /// <param name="operation5">An operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            GenOperation<Actual, Model> operation5,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4, operation5 },
                equal, seed, iter, time, threads, printActual, printModel);

        /// <summary>Sample model-based operations on a random initial state checking that actual and model are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state.</param>
        /// <param name="operation2">An operation generator that can act on the state.</param>
        /// <param name="operation3">An operation generator that can act on the state.</param>
        /// <param name="operation4">An operation generator that can act on the state.</param>
        /// <param name="operation5">An operation generator that can act on the state.</param>
        /// <param name="operation6">An operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
        /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
        public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
            GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
            GenOperation<Actual, Model> operation5, GenOperation<Actual, Model> operation6,
            Func<Actual, Model, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<Actual, string> printActual = null, Func<Model, string> printModel = null)
            => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
                equal, seed, iter, time, threads, printActual, printModel);

        class MetamorphicData<T> { public T State1; public T State2; public uint Stream; public ulong Seed; public Exception Exception; }

        /// <summary>Sample metamorphic (two path) operations on a random initial state checking that both paths are equal.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operations">A metamorphic operation generator that can act on the state.</param>
        /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        public static void SampleMetamorphic<T>(this Gen<T> initial, GenMetamorphic<T> operations,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1,
            Func<T, string> print = null)
        {
            if (equal is null) equal = ModelEqual;
            if (seed is null) seed = Seed;
            if (iter == -1) iter = Iter;
            if (time == -1) time = Time;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;

            Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                var i1 = initial.Generate(pcg, null, out size);
                var i2 = initial.Generate(new PCG(stream, seed), null, out size);
                return new MetamorphicData<T> { State1 = i1, State2 = i2, Stream = stream, Seed = seed };
            })
            .Select(operations)
            .Sample(d =>
            {
                try
                {
                    d.V1.Item2(d.V0.State1);
                    d.V1.Item3(d.V0.State2);
                    return equal(d.V0.State1, d.V0.State2);
                }
                catch (Exception e)
                {
                    d.V0.Exception = e;
                    return false;
                }
            }, seed, iter, time, threads,
            p =>
            {
                if (p.V0 == null) return "";
                var sb = new StringBuilder();
                var initialState = initial.Generate(new PCG(p.V0.Stream, p.V0.Seed), null, out _);
                sb.Append("\nInitial State: ").Append(print(initialState));
                sb.Append("\n   Operations: ").Append(p.V1.Item1);
                if (p.V0.Exception is null)
                {
                    sb.Append("\nFinal State 1: ").Append(print(p.V0.State1));
                    sb.Append("\nFinal State 2: ").Append(print(p.V0.State2));
                }
                else
                {
                    sb.Append("\n    Exception: ").Append(p.V0.Exception.ToString());
                }
                return sb.ToString();
            });
        }

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
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operations">The operation generators that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T>[] operations,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
        {
            if (equal is null) equal = Equal;
            if (seed is null) seed = Seed;
            if (iter == -1) iter = Iter;
            if (time == -1) time = Time;
            if (threads == -1) threads = Threads;
            if (print is null) print = Print;
            if (replay == -1) replay = Replay;
            int[] replayThreads = null;
            if (seed is not null && seed.Contains("["))
            {
                int i = seed.IndexOf('[');
                int j = seed.IndexOf(']', i + 1);
                replayThreads = seed.Substring(i + 1, j - i - 1).Split(',').Select(int.Parse).ToArray();
                seed = seed.Substring(0, i);
            }

            var opNameActions = new Gen<(string, Action<T>)>[operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                var op = operations[i];
                var opName = "Op" + i;
                opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2)) : op;
            }

            bool firstIteration = true;

            Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var stream = pcg.Stream;
                var seed = pcg.Seed;
                return (initial.Generate(pcg, null, out size), stream, seed);
            })
            .Select(Gen.OneOf(opNameActions).Array[1, MAX_CONCURRENT_OPERATIONS].SelectTuple(ops => Gen.Int[1, Math.Min(threads, ops.Length)]), (a, b) =>
                new ConcurrentData<T> { State = a.Item1, Stream = a.stream, Seed = a.seed, Operations = b.V0, Threads = b.V1 }
            )
            .Sample(cd =>
            {
                bool linearizable = false;
                do
                {
                    try
                    {
                        if (replayThreads is null)
                            Run(cd.State, cd.Operations, cd.Threads, cd.ThreadIds = new int[cd.Operations.Length]);
                        else
                            RunReplay(cd.State, cd.Operations, cd.Threads, cd.ThreadIds = replayThreads);
                    }
                    catch (Exception e)
                    {
                        cd.Exception = e;
                        break;
                    }
                    System.Threading.Tasks.Parallel.ForEach(Permutations(cd.ThreadIds, cd.Operations), (sequence, state) =>
                    {
                        var linearState = initial.Generate(new PCG(cd.Stream, cd.Seed), null, out _);
                        try
                        {
                            Run(linearState, sequence, 1);
                            if (equal(cd.State, linearState))
                            {
                                linearizable = true;
                                state.Stop();
                            }
                        }
                        catch { state.Stop(); }
                    });
                } while (linearizable && firstIteration && seed != null && --replay > 0);
                firstIteration = false;
                return linearizable;
            }, seed, iter, time, threads: 1,
            p =>
            {
                if (p == null) return "";
                var sb = new StringBuilder();
                sb.Append("\n   Operations: ").Append(Print(p.Operations.Select(i => i.Item1).ToList()));
                sb.Append("\n   On Threads: ").Append(Print(p.ThreadIds));
                sb.Append("\nInitial state: ").Append(print(initial.Generate(new PCG(p.Stream, p.Seed), null, out _)));
                sb.Append("\n  Final state: ").Append(p.Exception is not null ? p.Exception.ToString() : print(p.State));
                bool first = true;
                foreach (var sequence in Permutations(p.ThreadIds, p.Operations))
                {
                    var linearState = initial.Generate(new PCG(p.Stream, p.Seed), null, out _);
                    string result;
                    try
                    {
                        Run(linearState, sequence, 1);
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
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation }, equal, seed, iter, time, threads, print, replay);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation2">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation1, operation2 }, equal, seed, iter, time, threads, print, replay);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation2">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation3">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3 }, equal, seed, iter, time, threads, print, replay);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation2">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation3">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation4">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4 }, equal, seed, iter, time, threads, print, replay);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation2">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation3">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation4">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation5">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4, operation5 },
                equal, seed, iter, time, threads, print, replay);

        /// <summary>Sample model-based operations on a random initial state concurrently.
        /// The result is compared against the result of the possible sequential permutations.
        /// At least one of these permutations result must be equal for the concurrency to have been linearized successfully.
        /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
        /// <param name="initial">The initial state generator.</param>
        /// <param name="operation1">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation2">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation3">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation4">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation5">An operation generator that can act on the state concurrently.</param>
        /// <param name="operation6">An operation generator that can act on the state concurrently.</param>
        /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
        /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
        public static void SampleConcurrent<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2,
            GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5, GenOperation<T> operation6,
            Func<T, T, bool> equal = null, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string> print = null, int replay = -1)
            => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
                equal, seed, iter, time, threads, print, replay);

        /// <summary>Assert actual is in line with expected using a chi-squared test to 6 sigma.</summary>
        /// <param name="actual">The actual bin counts.</param>
        /// <param name="expected">The expected bin counts.</param>
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

        /// <summary>Assert the first Action is faster than the second to a given sigma.</summary>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster(Action faster, Action slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma;
            sigma *= sigma;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try
                    {
                        while (!mre.IsSet)
                        {
                            long tf = 0L, ts = 0L, st = 0L;
                            for (int i = 1; i < repeat; i++)
                            {
                                st = Stopwatch.GetTimestamp();
                                faster();
                                tf += Stopwatch.GetTimestamp() - st;
                                st = Stopwatch.GetTimestamp();
                                slower();
                                ts += Stopwatch.GetTimestamp() - st;
                                if (mre.IsSet) return;
                            }
                            st = Stopwatch.GetTimestamp();
                            faster();
                            tf += Stopwatch.GetTimestamp() - st;
                            st = Stopwatch.GetTimestamp();
                            slower();
                            ts += Stopwatch.GetTimestamp() - st;
                            if (mre.IsSet) return;
                            r.Add(tf, ts);
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                }, null);
            bool completed = mre.Wait(timeout * 1000);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster || r.Median.Median < 0.0) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma.</summary>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T>(Func<T> faster, Func<T> slower, Action<T, T> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma;
            sigma *= sigma;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    try
                    {
                        while (!mre.IsSet)
                        {
                            long tf = 0L, ts = 0L, st = 0L;
                            for (int i = 1; i < repeat; i++)
                            {
                                st = Stopwatch.GetTimestamp();
                                faster();
                                tf += Stopwatch.GetTimestamp() - st;
                                st = Stopwatch.GetTimestamp();
                                slower();
                                ts += Stopwatch.GetTimestamp() - st;
                                if (mre.IsSet) return;
                            }
                            st = Stopwatch.GetTimestamp();
                            var vf = faster();
                            tf += Stopwatch.GetTimestamp() - st;
                            st = Stopwatch.GetTimestamp();
                            var vs = slower();
                            ts += Stopwatch.GetTimestamp() - st;
                            if (mre.IsSet) return;
                            if (assertEqual is null)
                            {
                                if (!Equal(vf, vs))
                                {
                                    var vfs = Print(vf);
                                    vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                                    var vss = Print(vs);
                                    vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                                    exception = new CsCheckException("Return values differ:" + vfs + vss);
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
                            r.Add(tf, ts);
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                        mre.Set();
                    }
                }, null);
            bool completed = mre.Wait(timeout * 1000);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster || r.Median.Median < 0.0) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T>(this Gen<T> gen, Action<T> faster, Action<T> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma;
            sigma *= sigma; // using sigma as sigma squared now
            if (seed is null) seed = Seed;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(__ =>
                {
                    var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
                    ulong state = 0;
                    T t = default;
                    try
                    {
                        while (!mre.IsSet)
                        {
                            state = pcg.State;
                            t = gen.Generate(pcg, null, out _);
                            long tf = 0L, ts = 0L, st = 0L;
                            for (int i = 1; i < repeat; i++)
                            {
                                st = Stopwatch.GetTimestamp();
                                faster(t);
                                tf += Stopwatch.GetTimestamp() - st;
                                st = Stopwatch.GetTimestamp();
                                slower(t);
                                ts += Stopwatch.GetTimestamp() - st;
                                if (mre.IsSet) return;
                            }
                            st = Stopwatch.GetTimestamp();
                            faster(t);
                            tf += Stopwatch.GetTimestamp() - st;
                            st = Stopwatch.GetTimestamp();
                            slower(t);
                            ts += Stopwatch.GetTimestamp() - st;
                            if (mre.IsSet) return;
                            r.Add(tf, ts);
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        var tstring = Print(t);
                        if (tstring.Length > 100) tstring = tstring.Substring(0, 100);
                        exception = new CsCheckException("CsCheck_Seed = \"" + pcg.ToString(state) + "\" T=" + tstring, e);
                        mre.Set();
                    }
                }, null);
            var completed = mre.Wait(timeout * 1000);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster || r.Median.Median < 0.0) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> faster, Action<T1, T2> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> faster, Action<T1, T2, T3> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> faster, Action<T1, T2, T3, T4> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> faster, Action<T1, T2, T3, T4, T5> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Action<T1, T2, T3, T4, T5, T6> faster, Action<T1, T2, T3, T4, T5, T6> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Action is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Action<T1, T2, T3, T4, T5, T6, T7> faster, Action<T1, T2, T3, T4, T5, T6, T7> slower,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T, R>(this Gen<T> gen, Func<T, R> faster, Func<T, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
        {
            if (sigma == -1.0) sigma = Sigma;
            sigma *= sigma; // using sigma as sigma squared now
            if (seed is null) seed = Seed;
            if (threads == -1) threads = Threads;
            if (threads == -1) threads = Environment.ProcessorCount;
            var r = new FasterResult { Median = new MedianEstimator() };
            var mre = new ManualResetEventSlim();
            Exception exception = null;
            while (threads-- > 0)
                ThreadPool.UnsafeQueueUserWorkItem(__ =>
                {
                    var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
                    ulong state = 0;
                    T t = default;
                    try
                    {

                        while (!mre.IsSet)
                        {
                            state = pcg.State;
                            t = gen.Generate(pcg, null, out _);
                            long tf = 0L, ts = 0L, st = 0L;
                            for (int i = 1; i < repeat; i++)
                            {
                                st = Stopwatch.GetTimestamp();
                                faster(t);
                                tf += Stopwatch.GetTimestamp() - st;
                                st = Stopwatch.GetTimestamp();
                                slower(t);
                                ts += Stopwatch.GetTimestamp() - st;
                                if (mre.IsSet) return;
                            }
                            st = Stopwatch.GetTimestamp();
                            var vf = faster(t);
                            tf += Stopwatch.GetTimestamp() - st;
                            st = Stopwatch.GetTimestamp();
                            var vs = slower(t);
                            ts += Stopwatch.GetTimestamp() - st;
                            if (mre.IsSet) return;
                            if (assertEqual is null)
                            {
                                if (!Equal(vf, vs))
                                {
                                    var vfs = Print(vf);
                                    vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                                    var vss = Print(vs);
                                    vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                                    exception = new CsCheckException("Return values differ: CsCheck_Seed = \"" + pcg.ToString(state) + "\"" + vfs + vss);
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
                                    exception = new CsCheckException("Return values differ: CsCheck_Seed = \"" + pcg.ToString(state) + "\"", ex);
                                    mre.Set();
                                    return;
                                }
                            }
                            r.Add(tf, ts);
                            if (r.SigmaSquared >= sigma) mre.Set();
                        }
                    }
                    catch (Exception e)
                    {
                        var tstring = Print(t);
                        if (tstring.Length > 100) tstring = tstring.Substring(0, 100);
                        exception = new CsCheckException("CsCheck_Seed = \"" + pcg.ToString(state) + "\" T=" + tstring, e);
                        mre.Set();
                    }
                }, null);
            var completed = mre.Wait(timeout * 1000);
            if (raiseexception)
            {
                if (!completed) throw new CsCheckException("Timeout! " + r.ToString());
                if (exception is not null || r.Slower > r.Faster || r.Median.Median < 0.0) throw exception ?? new CsCheckException(r.ToString());
            }
            return r;
        }

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, R> faster, Func<T1, T2, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> faster, Func<T1, T2, T3, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> faster, Func<T1, T2, T3, T4, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> faster, Func<T1, T2, T3, T4, T5, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> faster, Func<T1, T2, T3, T4, T5, T6, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Assert the first Func gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
        /// <param name="gen">The input data generator.</param>
        /// <param name="faster">The presumed faster code to test.</param>
        /// <param name="slower">The presumed slower code to test.</param>
        /// <param name="assertEqual">An assert test of if the faster and slower code returns an equal value (default Check.Equal).</param>
        /// <param name="sigma">The sigma is the number of standard deviations from the null hypothosis (default 6).</param>
        /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
        /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
        /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
        public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> faster, Func<T1, T2, T3, T4, T5, T6, T7, R> slower, Action<R, R> assertEqual = null,
            double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = 60, string seed = null, bool raiseexception = true)
            => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), assertEqual, sigma, threads, repeat, timeout, seed, raiseexception);

        /// <summary>Generate an example that satisfies the predicate.</summary>
        /// <param name="gen">The data generator.</param>
        /// <param name="predicate">The predicate the data has to satisfy.</param>
        /// <param name="seed">The initial seed to use to pin the example once found.</param>
        public static T Example<T>(this Gen<T> gen, Func<T, bool> predicate, string seed = null)
        {
            if (seed is null)
            {
                var mre = new ManualResetEventSlim();
                T ret = default;
                string message = null;
                var threads = Environment.ProcessorCount;
                while (threads-- > 0)
                    ThreadPool.UnsafeQueueUserWorkItem(__ =>
                    {
                        var pcg = PCG.ThreadPCG;
                        while (true)
                        {
                            if (mre.IsSet) return;
                            var state = pcg.State;
                            var t = gen.Generate(pcg, null, out _);
                            if (predicate(t))
                            {
                                lock (mre)
                                {
                                    if (message is null)
                                    {
                                        message = "Example " + typeof(T).Name + " seed = \"" + pcg.ToString(state) + "\"";
                                        ret = t;
                                        mre.Set();
                                    }
                                }

                            }
                        }
                    }, null);
                mre.Wait();
                throw new CsCheckException(message);
            }
            else
            {
                var pcg = PCG.Parse(seed);
                var t = gen.Generate(pcg, null, out _);
                if (!predicate(t)) throw new CsCheckException("where clause no longer satisfied");
                return t;
            }
        }

        /// <summary>Check Equals, IEquatable and GetHashCode are consistent.</summary>
        /// <param name="gen">The sample input data generator.</param>
        /// <param name="seed">The initial seed to use for the first iteration.</param>
        /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
        /// <param name="time">The number of seconds to run the sample.</param>
        /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
        /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
        public static void Equality<T>(this Gen<T> gen, string seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T, T), string> print = null)
        {
            if (iter == -1) iter = Iter;
            if (iter > 1) iter /= 2;
            if (time == -1) time = Time;
            if (time > 1) time /= 2;

            gen.Clone().Sample((t1, t2) =>
                t1.Equals(t2) && t2.Equals(t1) && Equals(t1, t2) && t1.GetHashCode() == t2.GetHashCode()
                && (t1 is not IEquatable<T> e || (e.Equals(t2) && ((IEquatable<T>)t2).Equals(t1)))
            , seed, iter, time, threads, print);

            gen.Select(gen).Sample((t1, t2) =>
            {
                bool equal = t1.Equals(t2);
                return
                (!equal && !t2.Equals(t1) && !Equals(t1, t2)
                 && (t1 is not IEquatable<T> e2 || (!e2.Equals(t2) && !((IEquatable<T>)t2).Equals(t1))))
                ||
                (equal && t2.Equals(t1) && Equals(t1, t2) && t1.GetHashCode() == t2.GetHashCode()
                 && (t1 is not IEquatable<T> e || (e.Equals(t2) && ((IEquatable<T>)t2).Equals(t1))));
            }, seed, iter, time, threads, print);
        }

        /// <summary>Check a hash of a series of values. Cache values on a correct run and fail with stack trace at first difference.</summary>
        /// <param name="action">The code called to add values into the hash.</param>
        /// <param name="expected">The expected hash value set after an initial run to find it.</param>
        /// <param name="decimalPlaces">The number of decimal places to round for double, float and decimal.</param>
        /// <param name="significantFigures">The number of significant figures to round for double, float and decimal.</param>
        /// <param name="memberName">Automatically set to the method name.</param>
        /// <param name="filePath">Automatically set to the file path.</param>
        public static void Hash(Action<Hash> action, long expected = 0, int? decimalPlaces = null, int? significantFigures = null,
            [CallerMemberName] string memberName = "", [CallerFilePath] string filePath = "")
        {
            if (expected == 0)
            {
                var hash = new Hash(null, -1, decimalPlaces, significantFigures);
                action(hash);
                var offset = hash.BestOffset();
                hash = new Hash(null, offset, decimalPlaces, significantFigures);
                action(hash);
                var fullHashCode = CsCheck.Hash.FullHash(offset, hash.GetHashCode());
                throw new CsCheckException("Hash is " + fullHashCode);
            }
            else
            {
                var (offset, expectedHashCode) = CsCheck.Hash.OffsetHash(expected);

                // Check hash without opening the file if it already exists for better IO performance for most common code path.
                if (File.Exists(CsCheck.Hash.Filename(expected, memberName, filePath)))
                {
                    var hasher = new Hash(null, offset, decimalPlaces, significantFigures);
                    action(hasher);
                    if (hasher.GetHashCode() == expectedHashCode) return;
                }

                var hash = new Hash(expectedHashCode, offset, decimalPlaces, significantFigures, memberName, filePath);
                action(hash);
                int actualHashCode = hash.GetHashCode();
                hash.Close();
                if (actualHashCode != expectedHashCode)
                {
                    hash = new Hash(null, -1, decimalPlaces, significantFigures);
                    action(hash);
                    var offsetCheck = hash.BestOffset();
                    if (offsetCheck != offset)
                    {
                        offset = offsetCheck;
                        hash = new Hash(null, offset, decimalPlaces, significantFigures);
                        action(hash);
                        actualHashCode = hash.GetHashCode();
                    }
                    var actualFullHash = CsCheck.Hash.FullHash(offset, actualHashCode);
                    throw new CsCheckException("Actual " + actualFullHash + " but expected " + expected);
                }
            }
        }
    }

    public class FasterResult
    {
        public int Faster, Slower;
        public MedianEstimator Median;
        public float SigmaSquared
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
        public void Add(long faster, long slower)
        {
            lock (Median)
            {
                if (faster < slower)
                {
                    Faster++;
                    Median.Add(((double)(slower - faster)) / slower);
                }
                else if (slower < faster)
                {
                    Slower++;
                    Median.Add(((double)(slower - faster)) / faster);
                }
                else
                {
                    Median.Add(0.0);
                }
            }
        }
        public override string ToString()
        {
            var result = $"{Median.Median * 100.0:#0.0}%[{Median.LowerQuartile * 100.0:#0.0}%..{Median.UpperQuartile * 100.0:#0.0}%] ";
            result += Median.Median >= 0.0 ? "faster" : "slower";
            if (double.IsNaN(Median.Median)) result = "Time resolution too small try using repeat.\n" + result;
            else if ((Median.Median >= 0.0) != (Faster > Slower)) result = "Inconsistent result try using repeat or increasing sigma.\n" + result;
            return result + $", sigma={Math.Sqrt(SigmaSquared):#0.0} ({Faster:#,0} vs {Slower:#,0})";
        }
        public void Output(Action<string> output) => output(ToString());
    }
}