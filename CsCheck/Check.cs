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
using System.Runtime.CompilerServices;
using System.Text;
using System.Reflection;

/// <summary>Main random testing Check functions.</summary>
public static partial class Check
{
    /// <summary>The number of iterations to run in the sample (default 100).</summary>
    public static long Iter = ParseEnvironmentVariableToLong("CsCheck_Iter", 100);
    /// <summary>The number of seconds to run the sample.</summary>
    public static int Time = ParseEnvironmentVariableToInt("CsCheck_Time" , -1);
    /// <summary>The number of times to retry the seed to reproduce a SampleParallel fail (default 100).</summary>
    public static int Replay = ParseEnvironmentVariableToInt("CsCheck_Replay", 100);
    /// <summary>The number of threads to run the sample on (default number logical CPUs).</summary>
    public static int Threads = ParseEnvironmentVariableToInt("CsCheck_Threads", Environment.ProcessorCount);
    /// <summary>The initial seed to use for the first iteration.</summary>
    public static string? Seed = ParseEnvironmentVariableToSeed("CsCheck_Seed");
    /// <summary>The sigma to use for Faster (default 6).</summary>
    public static double Sigma = ParseEnvironmentVariableToDouble("CsCheck_Sigma", 6.0);
    /// <summary>The timeout in seconds to use for Faster (default 60 seconds).</summary>
    public static int Timeout = ParseEnvironmentVariableToInt("CsCheck_Timeout", 60);
    /// <summary>The number of ulps to approximate to when printing doubles and floats.</summary>
    public static int Ulps = ParseEnvironmentVariableToInt("CsCheck_Ulps", 4);
    /// <summary>The number of Where Gne iterations before throwing an exception.</summary>
    public static int WhereLimit = ParseEnvironmentVariableToInt("CsCheck_WhereLimit", 100);
    internal static bool IsDebug = Assembly.GetCallingAssembly().GetCustomAttribute<DebuggableAttribute>()?.IsJITTrackingEnabled ?? false;

    sealed class SampleActionWorker<T>(Gen<T> gen, Action<T> assert, CountdownEvent cde, string? seed, long target, bool isIter) : IThreadPoolWorkItem
    {
        public PCG? MinPCG;
        public ulong MinState;
        public Size? MinSize;
        public T MinT = default!;
        public Exception? MinException;
        public int Shrinks = -1;
        public long Total = seed is null ? 0 : 1;
        public long Skipped;

        public void Execute()
        {
            var pcg = PCG.ThreadPCG;
            Size? size = null;
            T t = default!;
            long skippedLocal = 0, totalLocal = 0;
            ulong state = 0;
            while (true)
            {
                try
                {
                    while (true)
                    {
                        if ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) < 0)
                        {
                            Interlocked.Add(ref Skipped, skippedLocal);
                            Interlocked.Add(ref Total, totalLocal);
                            cde.Signal();
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, MinSize, out size);
                        if (MinSize is null || Size.IsLessThan(size, MinSize))
                        {
                            assert(t);
                        }
                        else
                            skippedLocal++;
                    }
                }
                catch (Exception e)
                {
                    lock (cde)
                    {
                        if (MinSize is null || Size.IsLessThan(size, MinSize))
                        {
                            MinSize = size;
                            MinPCG = pcg;
                            MinState = state;
                            MinT = t;
                            MinException = e;
                            Shrinks++;
                        }
                    }
                }
            }
        }
        public string ExceptionMessage(Func<T, string> print)
        {
            return SampleErrorMessage(MinPCG!.ToString(MinState), print(MinT!), Shrinks, Skipped, Total);
        }
    }

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T>(this Gen<T> gen, Action<T> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null,
        ILogger? logger = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        bool isIter = time < 0;
        var cde = new CountdownEvent(threads);
        if (logger is not null)
            assert = logger.WrapAssert(assert);

        var worker = new SampleActionWorker<T>(
            gen,
            assert,
            cde,
            seed,
            isIter ? seed is null ? iter : iter - 1 : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency,
            isIter);

        if (seed is not null)
        {
            var pcg = PCG.Parse(seed);
            ulong state = pcg.State;
            Size? size = null;
            T t = default!;
            try
            {
                assert(t = gen.Generate(pcg, null, out size));
            }
            catch (Exception e)
            {
                worker.MinSize = size;
                worker.MinPCG = pcg;
                worker.MinState = state;
                worker.MinT = t;
                worker.MinException = e;
                worker.Shrinks++;
            }
        }

        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        cde.Wait();
        cde.Dispose();

        if (worker.MinPCG is not null)
            throw new CsCheckException(worker.ExceptionMessage(print ?? Print), worker.MinException);
        if (writeLine is not null) writeLine($"Passed {worker.Total:#,0} iterations.");
    }

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Action<T1, T2, T3, T4, T5, T6> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Action<T1, T2, T3, T4, T5, T6, T7> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Action<T1, T2, T3, T4, T5, T6, T7, T8> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T>(this Gen<T> gen, Func<T, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, t =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3, t4) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3, t4, t5) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, string> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3, t4, t5, t6) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, string> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3, t4, t5, t6, t7) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6, t7);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, string> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        Sample(gen, (t1, t2, t3, t4, t5, t6, t7, t8) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6, t7, t8);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, ILogger? logger = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        if (logger is not null)
            assert = logger.WrapAssert(assert);

        PCG? minPCG = null;
        ulong minState = 0UL;
        Size? minSize = null;
        T minT = default!;
        Exception? minException = null;

        int shrinks = -1;
        if (seed is not null)
        {
            var pcg = PCG.Parse(seed);
            ulong state = pcg.State;
            Size? size = null;
            T t = default!;
            try
            {
                await assert(t = gen.Generate(pcg, null, out size)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                minSize = size;
                minPCG = pcg;
                minState = state;
                minT = t;
                minException = e;
                shrinks++;
            }
        }
        long skipped = 0;
        bool isIter = time < 0;
        long target = isIter ? seed is null ? iter : iter - 1
                    : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
        long total = seed is null ? 0 : 1;
        var tasks = new Task[threads];
#pragma warning disable IDE0039 // Use local function - only want one delegate created
        var worker = async () =>
        {
            var pcg = PCG.ThreadPCG;
            Size? size = null;
            T t = default!;
            long skippedLocal = 0, totalLocal = 0;
            ulong state = 0;
            while (true)
            {
                try
                {
                    while (true)
                    {
                        if ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) < 0)
                        {
                            Interlocked.Add(ref skipped, skippedLocal);
                            Interlocked.Add(ref total, totalLocal);
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, minSize, out size);
                        if (minSize is null || Size.IsLessThan(size, minSize))
                            await assert(t).ConfigureAwait(false);
                        else
                            skippedLocal++;
                    }
                }
                catch (Exception e)
                {
                    lock (tasks)
                    {
                        if (minSize is null || Size.IsLessThan(size, minSize))
                        {
                            minSize = size;
                            minPCG = pcg;
                            minState = state;
                            minT = t;
                            minException = e;
                            shrinks++;
                        }
                    }
                }
            }
        };
#pragma warning restore IDE0039 // Use local function
        while (threads-- > 0)
            tasks[threads] = Task.Run(worker);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (minPCG is not null)
            throw new CsCheckException(SampleErrorMessage(minPCG.ToString(minState), (print ?? Print)(minT!), shrinks, skipped, total), minException);
        if (writeLine is not null) writeLine($"Passed {total:#,0} iterations.");
    }

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task> assert, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task> assert,
         Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null,
         ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task> assert,
         Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> assert,
         Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        ILogger? logger = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task<string>> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async t =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<string>> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<string>> classify, Action<string> writeLine,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<string>> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3, t4) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<string>> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3, t4, t5) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<string>> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3, t4, t5, t6) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<string>> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3, t4, t5, t6, t7) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6, t7).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<string>> classify,
        Action<string> writeLine, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        ILogger? logger = null)
    {
        var classifier = new Classifier();
        await SampleAsync(gen, async (t1, t2, t3, t4, t5, t6, t7, t8) =>
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6, t7, t8).ConfigureAwait(false);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }, null, seed, iter, time, threads, print, logger).ConfigureAwait(false);
        classifier.Print(writeLine);
    }

    sealed class SampleFuncWorker<T>(Gen<T> gen, Func<T, bool> predicate, CountdownEvent cde, string? seed, long target, bool isIter) : IThreadPoolWorkItem
    {
        public PCG? MinPCG;
        public ulong MinState;
        public Size? MinSize;
        public T MinT = default!;
        public Exception? MinException;
        public int Shrinks = -1;
        public long Total = seed is null ? 0 : 1;
        public long Skipped;
        public void Execute()
        {
            var pcg = PCG.ThreadPCG;
            Size? size = null;
            T t = default!;
            long skippedLocal = 0, totalLocal = 0;
            ulong state = 0;
            while (true)
            {
                try
                {
                    while (true)
                    {
                        if ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) < 0)
                        {
                            Interlocked.Add(ref Skipped, skippedLocal);
                            Interlocked.Add(ref Total, totalLocal);
                            cde.Signal();
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, MinSize, out size);
                        if (MinSize is null || Size.IsLessThan(size, MinSize))
                        {
                            if (!predicate(t))
                            {
                                lock (cde)
                                {
                                    if (MinSize is null || Size.IsLessThan(size, MinSize))
                                    {
                                        MinSize = size;
                                        MinPCG = pcg;
                                        MinState = state;
                                        MinT = t;
                                        MinException = null;
                                        Shrinks++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            skippedLocal++;
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (cde)
                    {
                        if (MinSize is null || Size.IsLessThan(size, MinSize))
                        {
                            MinSize = size;
                            MinPCG = pcg;
                            MinState = state;
                            MinT = t;
                            MinException = e;
                            Shrinks++;
                        }
                    }
                }
            }
        }
        public string ExceptionMessage(Func<T, string> print)
        {
            return SampleErrorMessage(MinPCG!.ToString(MinState), print(MinT), Shrinks, Skipped, Total);
        }
    }

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, ILogger? logger = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        if (logger is not null)
            predicate = logger.WrapAssert(predicate);
        bool isIter = time < 0;
        var cde = new CountdownEvent(threads);
        var worker = new SampleFuncWorker<T>(
            gen,
            predicate,
            cde,
            seed,
            isIter ? seed is null ? iter : iter - 1 : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency,
            isIter);
        if (seed is not null)
        {
            var pcg = PCG.Parse(seed);
            ulong state = pcg.State;
            Size? size = null;
            T t = default!;
            try
            {
                t = gen.Generate(pcg, null, out size);
                if (!predicate(t))
                {
                    worker.MinSize = size;
                    worker.MinPCG = pcg;
                    worker.MinState = state;
                    worker.MinT = t;
                    worker.Shrinks++;
                }
            }
            catch (Exception e)
            {
                worker.MinSize = size;
                worker.MinPCG = pcg;
                worker.MinState = state;
                worker.MinT = t;
                worker.MinException = e;
                worker.Shrinks++;
            }
        }
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        cde.Wait();
        cde.Dispose();
        if (worker.MinPCG is not null) throw new CsCheckException(worker.ExceptionMessage(print ?? Print), worker.MinException);
        if (writeLine is not null) writeLine($"Passed {worker.Total:#,0} iterations.");
    }

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, bool> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null, ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, bool> predicate,
        Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> predicate,
        Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        ILogger? logger = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task<bool>> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, ILogger? logger = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        if (logger is not null)
            predicate = logger.WrapAssert(predicate);

        PCG? minPCG = null;
        ulong minState = 0UL;
        Size? minSize = null;
        T minT = default!;
        Exception? minException = null;

        int shrinks = -1;
        if (seed is not null)
        {
            var pcg = PCG.Parse(seed);
            ulong state = pcg.State;
            Size? size = null;
            T t = default!;
            try
            {
                t = gen.Generate(pcg, null, out size);
                if (!await predicate(t).ConfigureAwait(false))
                {
                    minSize = size;
                    minPCG = pcg;
                    minState = state;
                    minT = t;
                    shrinks++;
                }
            }
            catch (Exception e)
            {
                minSize = size;
                minPCG = pcg;
                minState = state;
                minT = t;
                minException = e;
                shrinks++;
            }
        }
        long skipped = 0;
        bool isIter = time < 0;
        long target = isIter ? seed is null ? iter : iter - 1
                    : Stopwatch.GetTimestamp() + time * Stopwatch.Frequency;
        long total = seed is null ? 0 : 1;
        var tasks = new Task[threads];
#pragma warning disable IDE0039 // Use local function - only want one delegate created
        var worker = async () =>
        {
            var pcg = PCG.ThreadPCG;
            Size? size = null;
            T t = default!;
            long skippedLocal = 0, totalLocal = 0;
            ulong state = 0;
            while (true)
            {
                try
                {
                    while (true)
                    {
                        if ((isIter ? Interlocked.Decrement(ref target) : target - Stopwatch.GetTimestamp()) < 0)
                        {
                            Interlocked.Add(ref skipped, skippedLocal);
                            Interlocked.Add(ref total, totalLocal);
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, minSize, out size);
                        if (minSize is null || Size.IsLessThan(size, minSize))
                        {
                            if (!await predicate(t).ConfigureAwait(false))
                            {
                                lock (tasks)
                                {
                                    if (minSize is null || Size.IsLessThan(size, minSize))
                                    {
                                        minSize = size;
                                        minPCG = pcg;
                                        minState = state;
                                        minT = t;
                                        minException = null;
                                        shrinks++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            skippedLocal++;
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (tasks)
                    {
                        if (minSize is null || Size.IsLessThan(size, minSize))
                        {
                            minSize = size;
                            minPCG = pcg;
                            minState = state;
                            minT = t;
                            minException = e;
                            shrinks++;
                        }
                    }
                }
            }
        };
#pragma warning restore IDE0039 // Use local function
        while (threads-- > 0)
            tasks[threads] = Task.Run(worker);
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (minPCG is not null)
            throw new CsCheckException(SampleErrorMessage(minPCG.ToString(minState), (print ?? Print)(minT!), shrinks, skipped, total), minException);
        if (writeLine is not null) writeLine($"Passed {total:#,0} iterations.");
    }

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<bool>> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<bool>> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<bool>> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<bool>> predicate, Action<string>? writeLine = null,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null, ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<bool>> predicate,
        Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null,
        ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<bool>> predicate,
        Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), writeLine, seed, iter, time, threads, print, logger);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<bool>> predicate,
        Action<string>? writeLine = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        ILogger? logger = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), writeLine, seed, iter, time, threads, print, logger);

    sealed class ModelBasedData<Actual, Model>(Actual actualState, Model modelState, uint stream, ulong seed, (string, Action<Actual>, Action<Model>)[] operations)
    {
        public Actual ActualState = actualState; public Model ModelState = modelState; public uint Stream = stream; public ulong Seed = seed; public (string, Action<Actual>, Action<Model>)[] Operations = operations; public Exception? Exception;
    }

    sealed class GenInitial<Actual, Model>(Gen<(Actual, Model)> initial) : Gen<(Actual Actual, Model Model, uint Stream, ulong Seed)>
    {
        public override (Actual Actual, Model Model, uint Stream, ulong Seed) Generate(PCG pcg, Size? min, out Size size)
        {
            var stream = pcg.Stream;
            var seed = pcg.Seed;
            var (actual, model) = initial.Generate(pcg, null, out size);
            return (actual, model, stream, seed);
        }
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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model>[] operations,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
    {
        equal ??= ModelEqual;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        printActual ??= Print;
        printModel ??= Print;

        var opNameActions = new Gen<(string, Action<Actual>, Action<Model>)>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            var op = operations[i];
            var opName = "Op" + i;
            opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2, t.Item3)) : op;
        }

        new GenInitial<Actual, Model>(initial)
        .Select(Gen.OneOf(opNameActions).Array, (a, b) => new ModelBasedData<Actual, Model>(a.Actual, a.Model, a.Stream, a.Seed, b))
        .Sample(d =>
        {
            try
            {
                foreach (var operation in d.Operations)
                {
                    operation.Item2(d.ActualState);
                    operation.Item3(d.ModelState);
                }
                return equal(d.ActualState, d.ModelState);
            }
            catch (Exception e)
            {
                d.Exception = e;
                return false;
            }
        }, writeLine, seed, iter, time, threads,
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
                sb.Append("\n     Exception: ").Append(p.Exception);
            }
            return sb.ToString();
        }, logger);
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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation], equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
        GenOperation<Actual, Model> operation2,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation1, operation2], equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
        GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation1, operation2, operation3], equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
        GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation1, operation2, operation3, operation4], equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
        GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
        GenOperation<Actual, Model> operation5,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation1, operation2, operation3, operation4, operation5],
            equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1,
        GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4,
        GenOperation<Actual, Model> operation5, GenOperation<Actual, Model> operation6,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, Action<string>? writeLine = null, ILogger? logger = null)
        => SampleModelBased(initial, [operation1, operation2, operation3, operation4, operation5, operation6],
            equal, seed, iter, time, threads, printActual, printModel, writeLine, logger);

    sealed class MetamorphicData<T>(T state1, T state2, uint stream, ulong seed)
    {
        public T State1 = state1; public T State2 = state2; public uint Stream = stream; public ulong Seed = seed; public Exception? Exception;
    }

    sealed class GenMetamorphicData<T>(Gen<T> initial) : Gen<MetamorphicData<T>>
    {
        public override MetamorphicData<T> Generate(PCG pcg, Size? min, out Size size)
        {
            var stream = pcg.Stream;
            var seed = pcg.Seed;
            var i1 = initial.Generate(pcg, null, out _);
            var i2 = initial.Generate(new PCG(stream, seed), null, out size);
            return new MetamorphicData<T>(i1, i2, stream, seed);
        }
    }
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
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    /// <param name="logger">Log metrics regarding generated inputs and results.</param>
    public static void SampleMetamorphic<T>(this Gen<T> initial, GenMetamorphic<T> operations,
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<T, string>? print = null, Action<string>? writeLine = null, ILogger? logger = null)
    {
        equal ??= ModelEqual;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;

        new GenMetamorphicData<T>(initial)
        .Select(operations)
        .Sample(d =>
        {
            try
            {
                d.Item2.Item2(d.Item1.State1);
                d.Item2.Item3(d.Item1.State2);
                return equal(d.Item1.State1, d.Item1.State2);
            }
            catch (Exception e)
            {
                d.Item1.Exception = e;
                return false;
            }
        }, writeLine, seed, iter, time, threads,
        p =>
        {
            print ??= Print;
            if (p.Item1 is null) return "";
            var sb = new StringBuilder();
            var initialState = initial.Generate(new PCG(p.Item1.Stream, p.Item1.Seed), null, out _);
            sb.Append("\nInitial State: ").Append(print(initialState));
            sb.Append("\n   Operations: ").Append(p.Item2.Item1);
            if (p.Item1.Exception is null)
            {
                sb.Append("\nFinal State 1: ").Append(print(p.Item1.State1));
                sb.Append("\nFinal State 2: ").Append(print(p.Item1.State2));
            }
            else
            {
                sb.Append("\n    Exception: ").Append(p.Item1.Exception);
            }
            return sb.ToString();
        }, logger);
    }

    sealed class SampleParallelData<T>(T state, uint stream, ulong seed, (string, Action<T>)[] sequentialOperations, (string, Action<T>)[] parallelOperations, int threads)
    {
        public T InitialState = state;
        public uint Stream = stream;
        public ulong Seed = seed;
        public (string, Action<T>)[] SequentialOperations = sequentialOperations;
        public (string, Action<T>)[] ParallelOperations = parallelOperations;
        public int Threads = threads;
        public int[]? ThreadIds;
        public Exception? Exception;
    }

    sealed class SampleParallelData<Actual, Model>(Actual actual, Model model, uint stream, ulong seed, (string, Action<Actual>, Action<Model>)[] sequentialOperations, (string, Action<Actual>, Action<Model>)[] parallelOperations, int threads)
    {
        public Actual InitialActual = actual;
        public Model InitialModel = model;
        public uint Stream = stream;
        public ulong Seed = seed;
        public (string, Action<Actual>, Action<Model>)[] SequentialOperations = sequentialOperations;
        public (string, Action<Actual>, Action<Model>)[] ParallelOperations = parallelOperations;
        public int Threads = threads;
        public int[]? ThreadIds;
        public Exception? Exception;
    }

    sealed class GenSampleParallel<T>(Gen<T> initial) : Gen<(T Value, uint Stream, ulong Seed)>
    {
        public override (T, uint, ulong) Generate(PCG pcg, Size? min, out Size size)
        {
            var stream = pcg.Stream;
            var seed = pcg.Seed;
            return (initial.Generate(pcg, null, out size), stream, seed);
        }
    }

    sealed class GenSampleParallel<Actual, Model>(Gen<(Actual, Model)> initial) : Gen<(Actual Actual, Model Model, uint Stream, ulong Seed)>
    {
        public override (Actual, Model, uint, ulong) Generate(PCG pcg, Size? min, out Size size)
        {
            var stream = pcg.Stream;
            var seed = pcg.Seed;
            var (actual, model) = initial.Generate(pcg, null, out size);
            return (actual, model, stream, seed);
        }
    }

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operations">The operation generators that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T>[] operations, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
    {
        equal ??= Equal;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        if (replay == -1) replay = Replay;
        int[]? replayThreads = null;
        if (seed?.Contains('[') == true)
        {
            int i = seed.IndexOf('[');
            int j = seed.IndexOf(']', i + 1);
            replayThreads = Array.ConvertAll(seed.Substring(i + 1, j - i - 1).Split(','), int.Parse);
            seed = seed[..i];
        }

        var opNameActions = new Gen<(string, Action<T>)>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            var op = operations[i];
            var opName = "Op" + i;
            opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2)) : op;
        }

        bool firstIteration = true;

        var genOps = Gen.OneOf(opNameActions);
        Gen.Int[2, maxParallelOperations]
        .SelectMany(np => Gen.Int[2, Math.Min(threads, np)].Select(nt => (nt, np)))
        .SelectMany((nt, np) => Gen.Int[0, maxSequentialOperations].Select(ns => (ns, nt, np)))
        .SelectMany((ns, nt, np) => new GenSampleParallel<T>(initial).Select(genOps.Array[ns], genOps.Array[np])
                                    .Select((initial, sequential, parallel) => (initial, sequential, nt, parallel)))
        .Select((initial, sequential, threads, parallel) => new SampleParallelData<T>(initial.Value, initial.Stream, initial.Seed, sequential, parallel, threads))
        .Sample(spd =>
        {
            bool linearizable = false;
            do
            {
                try
                {
                    if (replayThreads is null)
                        Run(spd.InitialState, spd.SequentialOperations, spd.ParallelOperations, spd.Threads, spd.ThreadIds = new int[spd.ParallelOperations.Length]);
                    else
                        RunReplay(spd.InitialState, spd.SequentialOperations, spd.ParallelOperations, spd.Threads, spd.ThreadIds = replayThreads);
                }
                catch (Exception e)
                {
                    spd.Exception = e;
                    break;
                }
                Parallel.ForEach(Permutations(spd.ThreadIds, spd.ParallelOperations), (sequence, state) =>
                {
                    var linearState = initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _);
                    try
                    {
                        Run(linearState, spd.SequentialOperations, sequence, 1);
                        if (equal(spd.InitialState, linearState))
                        {
                            linearizable = true;
                            state.Stop();
                        }
                    }
                    catch { state.Stop(); }
                });
            } while (linearizable && firstIteration && seed is not null && --replay > 0);
            firstIteration = false;
            return linearizable;
        }, writeLine, seed, iter, time, threads: 1,
        spd =>
        {
            print ??= Print;
            if (spd == null) return "";
            var sb = new StringBuilder();
            sb.Append("\n        Initial state: ").Append(print(initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _)));
            sb.Append("\nSequential Operations: ").Append(Print(spd.SequentialOperations.Select(i => i.Item1).ToList()));
            sb.Append("\n  Parallel Operations: ").Append(Print(spd.ParallelOperations.Select(i => i.Item1).ToList()));
            sb.Append("\n           On Threads: ").Append(Print(spd.ThreadIds));
            sb.Append("\n          Final state: ").Append(spd.Exception is not null ? spd.Exception.ToString() : print(spd.InitialState));
            bool first = true;
            foreach (var sequence in Permutations(spd.ThreadIds!, spd.ParallelOperations))
            {
                var linearState = initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _);
                string result;
                try
                {
                    Run(linearState, spd.SequentialOperations, sequence, 1);
                    result = print(linearState);
                }
                catch (Exception e)
                {
                    result = e.ToString();
                }
                sb.Append(first ? "\n           Linearized: " : "\n                     : ");
                sb.Append(Print(sequence.Select(i => i.Item1).ToList()));
                sb.Append(" -> ");
                sb.Append(result);
                first = false;
            }
            return sb.ToString();
        });
    }

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation1">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation2">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation1">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation2">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation3">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2, GenOperation<T> operation3, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation1">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation2">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation3">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation4">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2, GenOperation<T> operation3, GenOperation<T> operation4, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation1">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation2">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation3">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation4">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation5">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2, GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null,
        int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4, operation5], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on a random initial state in parallel.
    /// The result is compared against the result of the possible sequential permutations.
    /// At least one of these permutations result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial state generator.</param>
    /// <param name="operation1">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation2">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation3">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation4">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation5">An operation generator that can act on the state in parallel.</param>
    /// <param name="operation6">An operation generator that can act on the state in parallel.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<T>(this Gen<T> initial, GenOperation<T> operation1, GenOperation<T> operation2, GenOperation<T> operation3, GenOperation<T> operation4, GenOperation<T> operation5, GenOperation<T> operation6, Func<T, T, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4, operation5, operation6], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, print, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operations">The actual and model operation generators that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model>[] operations, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
    {
        equal ??= ModelEqual;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        if (replay == -1) replay = Replay;
        int[]? replayThreads = null;
        printActual ??= Print;
        printModel ??= Print;
        if (seed?.Contains('[') == true)
        {
            int i = seed.IndexOf('[');
            int j = seed.IndexOf(']', i + 1);
            replayThreads = Array.ConvertAll(seed.Substring(i + 1, j - i - 1).Split(','), int.Parse);
            seed = seed[..i];
        }

        var opNameActions = new Gen<(string, Action<Actual>, Action<Model>)>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            var op = operations[i];
            var opName = "Op" + i;
            opNameActions[i] = op.AddOpNumber ? op.Select((name, actual, model) => (opName + name, actual, model)) : op;
        }

        bool firstIteration = true;

        var genOps = Gen.OneOf(opNameActions);
        Gen.Int[2, maxParallelOperations]
        .SelectMany(np => Gen.Int[2, Math.Min(threads, np)].Select(nt => (nt, np)))
        .SelectMany((nt, np) => Gen.Int[0, maxSequentialOperations].Select(ns => (ns, nt, np)))
        .SelectMany((ns, nt, np) => new GenSampleParallel<Actual, Model>(initial).Select(genOps.Array[ns], genOps.Array[np])
                                    .Select((initial, sequential, parallel) => (initial, sequential, nt, parallel)))
        .Select((initial, sequential, threads, parallel) => new SampleParallelData<Actual, Model>(initial.Actual, initial.Model, initial.Stream, initial.Seed, sequential, parallel, threads))
        .Sample(spd =>
        {
            bool linearizable = false;
            do
            {
                var actualSequentialOperations = Array.ConvertAll(spd.SequentialOperations, i => (i.Item1, i.Item2));
                var actualParallelOperations = Array.ConvertAll(spd.ParallelOperations, i => (i.Item1, i.Item2));
                try
                {
                    if (replayThreads is null)
                        Run(spd.InitialActual, actualSequentialOperations, actualParallelOperations, spd.Threads, spd.ThreadIds = new int[spd.ParallelOperations.Length]);
                    else
                        RunReplay(spd.InitialActual, actualSequentialOperations, actualParallelOperations, spd.Threads, spd.ThreadIds = replayThreads);
                }
                catch (Exception e)
                {
                    spd.Exception = e;
                    break;
                }
                var modelSequentialOperations = Array.ConvertAll(spd.SequentialOperations, i => (i.Item1, i.Item3));
                var modelParallelOperations = Array.ConvertAll(spd.ParallelOperations, i => (i.Item1, i.Item3));
                Parallel.ForEach(Permutations(spd.ThreadIds, modelParallelOperations), (sequence, state) =>
                {
                    var (_, initialModel) = initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _);
                    try
                    {
                        Run(initialModel, modelSequentialOperations, sequence, 1);
                        if (equal(spd.InitialActual, initialModel))
                        {
                            linearizable = true;
                            state.Stop();
                        }
                    }
                    catch { state.Stop(); }
                });
            } while (linearizable && firstIteration && seed is not null && --replay > 0);
            firstIteration = false;
            return linearizable;
        }, writeLine, seed, iter, time, threads: 1,
        spd =>
        {
            if (spd == null) return "";
            var sb = new StringBuilder();
            sb.Append("\n        Initial state: ").Append(printActual(initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _).Item1));
            sb.Append("\nSequential Operations: ").Append(Print(spd.SequentialOperations.Select(i => i.Item1).ToList()));
            sb.Append("\n  Parallel Operations: ").Append(Print(spd.ParallelOperations.Select(i => i.Item1).ToList()));
            sb.Append("\n           On Threads: ").Append(Print(spd.ThreadIds));
            sb.Append("\n          Final state: ").Append(spd.Exception is not null ? spd.Exception.ToString() : printActual(spd.InitialActual));
            var modelSequentialOperations = Array.ConvertAll(spd.SequentialOperations, i => (i.Item1, i.Item3));
            var modelParallelOperations = Array.ConvertAll(spd.ParallelOperations, i => (i.Item1, i.Item3));
            bool first = true;
            foreach (var sequence in Permutations(spd.ThreadIds!, modelParallelOperations))
            {
                var (_, initialModel) = initial.Generate(new PCG(spd.Stream, spd.Seed), null, out _);
                string result;
                try
                {
                    Run(initialModel, modelSequentialOperations, sequence, 1);
                    result = printModel(initialModel);
                }
                catch (Exception e)
                {
                    result = e.ToString();
                }
                sb.Append(first ? "\n           Linearized: " : "\n                     : ");
                sb.Append(Print(sequence.Select(i => i.Item1).ToList()));
                sb.Append(" -> ");
                sb.Append(result);
                first = false;
            }
            return sb.ToString();
        });
    }

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation1">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation2">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1, GenOperation<Actual, Model> operation2, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation1">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation2">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation3">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1, GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation1">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation2">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation3">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation4">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1, GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation1">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation2">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation3">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation4">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation5">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1, GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4, GenOperation<Actual, Model> operation5, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4, operation5], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Sample operations on the random initial actual state in parallel and compare to all the possible linearized operations run sequentially on the initial model state.
    /// At least one of these permutations model result must be equal for the parallel execution to have been linearized successfully.
    /// If not the failing initial state and sequence will be shrunk down to the shortest and simplest.</summary>
    /// <param name="initial">The initial actual and model state generator.</param>
    /// <param name="operation1">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation2">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation3">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation4">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation5">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="operation6">An actual and model operation generator that can act on the state in parallel. There is no need for the model operations to be thread safe as they are only run sequentially.</param>
    /// <param name="equal">A function to check if the actual and model are the same (default Check.ModelEqual).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="maxSequentialOperations">The maximum number of operations to run sequentially before the parallel operations (default of 10).</param>
    /// <param name="maxParallelOperations">The maximum number of operations to run in parallel (default of 5).</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="printActual">A function to convert the actual state to a string for error reporting (default Check.Print).</param>
    /// <param name="printModel">A function to convert the model state to a string for error reporting (default Check.Print).</param>
    /// <param name="replay">The number of times to retry the seed to reproduce an initial fail (default 100).</param>
    /// <param name="writeLine">WriteLine function to use for the summary total iterations output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SampleParallel<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model> operation1, GenOperation<Actual, Model> operation2, GenOperation<Actual, Model> operation3, GenOperation<Actual, Model> operation4, GenOperation<Actual, Model> operation5, GenOperation<Actual, Model> operation6, Func<Actual, Model, bool>? equal = null, string? seed = null,
        int maxSequentialOperations = 10, int maxParallelOperations = 5, long iter = -1, int time = -1, int threads = -1, Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null, int replay = -1, Action<string>? writeLine = null)
        => SampleParallel(initial, [operation1, operation2, operation3, operation4, operation5, operation6], equal, seed, maxSequentialOperations, maxParallelOperations, iter, time, threads, printActual, printModel, replay, writeLine);

    /// <summary>Assert actual is in line with expected using a chi-squared test to sigma.</summary>
    /// <param name="expected">The expected bin counts.</param>
    /// <param name="actual">The actual bin counts.</param>
    /// <param name="sigma">Sigma, default of 6.</param>
    public static void ChiSquared(int[] expected, int[] actual, double sigma = 6.0)
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
        if (sigmaSquared > sigma * sigma) throw new CsCheckException("Chi-squared standard deviation = " + Math.Sqrt(sigmaSquared).ToString("0.0"));
    }

    sealed class FasterActionWorker(ITimerAction fasterTimer, ITimerAction slowerTimer, FasterResult result, long endTimestamp, bool raiseexception) : IThreadPoolWorkItem
    {
        volatile bool running = true;
        public void Execute()
        {
            try
            {
                while (running)
                {
                    if (result.Add(fasterTimer.Time(), slowerTimer.Time()))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                result.Exception = e;
                running = false;
            }
        }
    }
    /// <summary>Assert the first function is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster(Action faster, Action slower, double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true,
        Action<string>? writeLine = null)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterActionWorker(
            Timer.Create(faster, repeat),
            Timer.Create(slower, repeat),
            result,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster<I1, I2>(I1 faster, I2 slower, double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true,
        Action<string>? writeLine = null) where I1 : IInvoke where I2 : IInvoke
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterActionWorker(
            Timer.Create(faster, repeat),
            Timer.Create(slower, repeat),
            result,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    sealed class FasterFuncWorker<T>(ITimerFunc<T> fasterTimer, ITimerFunc<T> slowerTimer, FasterResult result, Func<T, T, bool> equal, long endTimestamp, bool raiseexception) : IThreadPoolWorkItem
    {
        volatile bool running = true;
        public void Execute()
        {
            try
            {
                while (running)
                {
                    if (result.Add(fasterTimer.Time(out var fasterValue), slowerTimer.Time(out var slowerValue)))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && !equal(fasterValue, slowerValue))
                    {
                        var vfs = Print(fasterValue);
                        vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                        var vss = Print(slowerValue);
                        vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                        result.Exception ??= new CsCheckException($"Return values differ:{vfs}{vss}");
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                result.Exception = e;
                running = false;
            }
        }
    }
    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster<T>(Func<T> faster, Func<T> slower, Func<T, T, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true, Action<string>? writeLine = null)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterFuncWorker<T>(
            Timer.Create(faster, repeat),
            Timer.Create(slower, repeat),
            result,
            equal ?? Equal,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (raiseexception && result.NotFaster && !IsDebug)
            throw new CsCheckException(result.ToString());
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static async Task FasterAsync(Func<Task> faster, Func<Task> slower, double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1,
        bool raiseexception = true, Action<string>? writeLine = null)
    {
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var running = true;
        async Task Worker()
        {
            try
            {
                while (running)
                {
                    if (result.Add(await fasterTimer.Time().ConfigureAwait(false), await slowerTimer.Time().ConfigureAwait(false)))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                result.Exception = e;
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker().ConfigureAwait(false);
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static async Task FasterAsync<T>(Func<Task<T>> faster, Func<Task<T>> slower, Func<T, T, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true, Action<string>? writeLine = null)
    {
        equal ??= Equal;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var running = true;
        async Task Worker()
        {
            try
            {
                while (running)
                {
                    var (fasterTime, fasterValue) = await fasterTimer.Time().ConfigureAwait(false);
                    var (slowerTime, slowerValue) = await slowerTimer.Time().ConfigureAwait(false);
                    if (result.Add(fasterTime, slowerTime))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && !equal(fasterValue, slowerValue))
                    {
                        var vfs = Print(fasterValue);
                        vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                        var vss = Print(slowerValue);
                        vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                        result.Exception ??= new CsCheckException($"Return values differ:{vfs}{vss}");
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                result.Exception = e;
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker().ConfigureAwait(false);
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    sealed class FasterActionWorker<T>(Gen<T> gen, ITimerAction<T> fasterTimer, ITimerAction<T> slowerTimer, FasterResult result, long endTimestamp, string? seed, bool raiseexception) : IThreadPoolWorkItem
    {
        volatile bool running = true;
        public void Execute()
        {
            var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
            ulong state = 0;
            T t = default!;
            try
            {
                while (running)
                {
                    state = pcg.State;
                    t = gen.Generate(pcg, null, out _);
                    if (running && result.Add(fasterTimer.Time(t), slowerTimer.Time(t)))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                var tString = Print(t);
                if (tString.Length > 300) tString = tString[..300];
                result.Exception = new CsCheckException($"CsCheck_Seed={pcg.ToString(state)} T={tString}", e);
                running = false;
            }
        }
    }
    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster<T>(this Gen<T> gen, Action<T> faster, Action<T> slower, double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1,
        string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterActionWorker<T>(
            gen,
            Timer.Create(faster, repeat),
            Timer.Create(slower, repeat),
            result,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            seed ?? Seed,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> faster, Action<T1, T2> slower, double sigma = -1.0, int threads = -1, int repeat = 1,
        int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> faster, Action<T1, T2, T3> slower, double sigma = -1.0, int threads = -1,
        int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> faster, Action<T1, T2, T3, T4> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> faster, Action<T1, T2, T3, T4, T5> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Action<T1, T2, T3, T4, T5, T6> faster, Action<T1, T2, T3, T4, T5, T6> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Action<T1, T2, T3, T4, T5, T6, T7> faster, Action<T1, T2, T3, T4, T5, T6, T7> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Action<T1, T2, T3, T4, T5, T6, T7, T8> faster, Action<T1, T2, T3, T4, T5, T6, T7, T8> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static async Task FasterAsync<T>(this Gen<T> gen, Func<T, Task> faster, Func<T, Task> slower, double sigma = -1.0, int threads = -1,
        int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
    {
        seed ??= Seed;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var running = true;
        async Task Worker()
        {
            var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
            ulong state = 0;
            T t = default!;
            try
            {
                while (running)
                {
                    state = pcg.State;
                    t = gen.Generate(pcg, null, out _);
                    if (!running) return;
                    if (result.Add(await fasterTimer.Time(t).ConfigureAwait(false), await slowerTimer.Time(t).ConfigureAwait(false)))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                var tString = Print(t);
                if (tString.Length > 300) tString = tString[..300];
                result.Exception = new CsCheckException($"CsCheck_Seed={pcg.ToString(state)} T={tString}", e);
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker().ConfigureAwait(false);
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task> faster, Func<T1, T2, Task> slower, double sigma = -1.0, int threads = -1,
        int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task> faster, Func<T1, T2, T3, Task> slower, double sigma = -1.0, int threads = -1,
        int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task> faster, Func<T1, T2, T3, T4, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task> faster, Func<T1, T2, T3, T4, T5, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task> faster, Func<T1, T2, T3, T4, T5, T6, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task> faster, Func<T1, T2, T3, T4, T5, T6, T7, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7),
            sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), sigma, threads, repeat, timeout, seed, raiseexception);

    sealed class FasterFuncWorker<T, R>(Gen<T> gen, ITimerFunc<T, R> fasterTimer, ITimerFunc<T, R> slowerTimer, FasterResult result, long endTimestamp, Func<R, R, bool> equal, string? seed, bool raiseexception) : IThreadPoolWorkItem
    {
        volatile bool running = true;
        public void Execute()
        {
            var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
            ulong state = 0;
            T t = default!;
            try
            {
                while (running)
                {
                    state = pcg.State;
                    t = gen.Generate(pcg, null, out _);
                    if (result.Add(fasterTimer.Time(t, out var fasterValue), slowerTimer.Time(t, out var slowerValue)))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && !equal(fasterValue, slowerValue))
                    {
                        var vfs = Print(fasterValue);
                        vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                        var vss = Print(slowerValue);
                        vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                        result.Exception ??= new CsCheckException($"Return values differ: CsCheck_Seed={pcg.ToString(state)}{vfs}{vss}");
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                var tString = Print(t);
                if (tString.Length > 300) tString = tString[..300];
                result.Exception = new CsCheckException($"CsCheck_Seed={pcg.ToString(state)} T={tString}", e);
                running = false;
            }
        }
    }
    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster<T, R>(this Gen<T> gen, Func<T, R> faster, Func<T, R> slower, Func<R, R, bool>? equal = null, double sigma = -1.0, int threads = -1,
        int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterFuncWorker<T, R>(
            gen,
            Timer.Create(faster, repeat),
            Timer.Create(slower, repeat),
            result,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            equal ?? Equal,
            seed ?? Seed,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static void Faster<I1, I2, T, R>(this Gen<T> gen, I1 faster, I2 slower, Func<R, R, bool>? equal = null, double sigma = -1.0, int threads = -1, int repeat = 1,
        int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
            where I1 : IInvoke<T, R> where I2 : IInvoke<T, R>
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var worker = new FasterFuncWorker<T, R>(
            gen,
            Timer.Create<I1, T, R>(faster, repeat),
            Timer.Create<I2, T, R>(slower, repeat),
            result,
            Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency,
            equal ?? Equal,
            seed ?? Seed,
            raiseexception);
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, R> faster, Func<T1, T2, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> faster, Func<T1, T2, T3, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> faster, Func<T1, T2, T3, T4, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), equal, sigma, threads, repeat, timeout, seed,
            raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> faster, Func<T1, T2, T3, T4, T5, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), equal, sigma, threads, repeat,
            timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> faster, Func<T1, T2, T3, T4, T5, T6, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), equal, sigma,
            threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> faster, Func<T1, T2, T3, T4, T5, T6, T7, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Faster<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    public static async Task FasterAsync<T, R>(this Gen<T> gen, Func<T, Task<R>> faster, Func<T, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
    {
        seed ??= Seed;
        equal ??= Equal;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var result = new FasterResult(sigma == -1 ? Sigma : sigma, repeat);
        var running = true;
        async Task Worker()
        {
            var pcg = seed is null ? PCG.ThreadPCG : PCG.Parse(seed);
            ulong state = 0;
            T t = default!;
            try
            {
                while (running)
                {
                    state = pcg.State;
                    t = gen.Generate(pcg, null, out _);
                    if (!running) return;
                    var (fasterTime, fasterValue) = await fasterTimer.Time(t).ConfigureAwait(false);
                    var (slowerTime, slowerValue) = await slowerTimer.Time(t).ConfigureAwait(false);
                    if (result.Add(fasterTime, slowerTime))
                    {
                        if (raiseexception && result.NotFaster && !IsDebug)
                            result.Exception ??= new CsCheckException(result.ToString());
                        running = false;
                        return;
                    }
                    if (running && !equal(fasterValue, slowerValue))
                    {
                        var vfs = Print(fasterValue);
                        vfs = vfs.Length > 30 ? "\nFaster=" + vfs : " Faster=" + vfs;
                        var vss = Print(slowerValue);
                        vss = vss.Length > 30 ? "\nSlower=" + vss : " Slower=" + vss;
                        result.Exception ??= new CsCheckException($"Return values differ:{vfs}{vss}");
                        running = false;
                        return;
                    }
                    if (running && Stopwatch.GetTimestamp() > endTimestamp)
                    {
                        if (raiseexception)
                            result.Exception ??= new CsCheckException($"Timeout! {result}");
                        running = false;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                var tString = Print(t);
                if (tString.Length > 300) tString = tString[..300];
                result.Exception = new CsCheckException($"CsCheck_Seed={pcg.ToString(state)} T={tString}", e);
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker().ConfigureAwait(false);
        if (result.Exception is not null) throw result.Exception;
        if (writeLine is not null) result.Output(writeLine);
    }

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<R>> faster, Func<T1, T2, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<R>> faster, Func<T1, T2, T3, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<R>> faster, Func<T1, T2, T3, T4, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<R>> faster, Func<T1, T2, T3, T4, T5, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, T7, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Assert the first function gives the same result and is faster than the second to a given sigma (defaults to 6) across a sample of input data.</summary>
    /// <param name="gen">The input data generator.</param>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="equal">A function to check if the two states are the same (default Check.Equal).</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60).</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    /// <param name="writeLine">WriteLine function to use for the summary output.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task FasterAsync<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true, Action<string>? writeLine = null)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8),
            equal, sigma, threads, repeat, timeout, seed, raiseexception, writeLine);

    /// <summary>Generate a single random example.</summary>
    /// <param name="gen">The data generator.</param>
    public static T Single<T>(this Gen<T> gen)
        => gen.Generate(PCG.ThreadPCG, null, out _);

    sealed class SingleWorker<T>(Gen<T> gen, Func<T, bool> predicate) : IThreadPoolWorkItem
    {
        public volatile string? message;
        public void Execute()
        {
            var pcg = PCG.ThreadPCG;
            while (message is null)
            {
                var state = pcg.State;
                var t = gen.Generate(pcg, null, out var _);
                if (predicate(t))
                    message = $"Example {typeof(T).Name} seed = \"{pcg.ToString(state)}\"";
            }
        }
    }
    /// <summary>Generate a single random example that satisfies the predicate. Throws giving the seed to use.</summary>
    /// <param name="gen">The data generator.</param>
    /// <param name="predicate">The predicate the data has to satisfy.</param>
    public static T Single<T>(this Gen<T> gen, Func<T, bool> predicate)
    {
        var worker = new SingleWorker<T>(gen, predicate);
        var threads = Environment.ProcessorCount;
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(worker, false);
        worker.Execute();
        throw new CsCheckException(worker.message!);
    }

    /// <summary>Generate a single example using the seed and checking that it still satisfies the predicate.</summary>
    /// <param name="gen">The data generator.</param>
    /// <param name="predicate">The predicate the data has to satisfy.</param>
    /// <param name="seed">The initial seed to use to pin the example once found.</param>
    public static T Single<T>(this Gen<T> gen, Func<T, bool> predicate, string seed)
    {
        var t = gen.Generate(PCG.Parse(seed), null, out _);
        if (predicate(t)) return t;
        throw new CsCheckException("predicate no longer satisfied");
    }

    /// <summary>Check Equals, <see cref="IEquatable{T}"/> and GetHashCode are consistent.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static void Equality<T>(this Gen<T> gen, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T, T), string>? print = null)
    {
        if (iter == -1) iter = Iter;
        if (iter > 1) iter /= 2;
        if (time == -1) time = Time;
        if (time > 1) time /= 2;

        gen.Clone().Sample((t1, t2) =>
            t1!.Equals(t2) && t2!.Equals(t1) && Equals(t1, t2) && t1.GetHashCode() == t2.GetHashCode()
            && (t1 is not IEquatable<T> e || (e.Equals(t2) && ((IEquatable<T>)t2).Equals(t1)))
        , null, seed, iter, time, threads, print);

        gen.Select(gen).Sample((t1, t2) =>
        {
            bool equal = t1!.Equals(t2);
            return
            (!equal && !t2!.Equals(t1) && !Equals(t1, t2)
             && (t1 is not IEquatable<T> e2 || (!e2.Equals(t2) && !((IEquatable<T>)t2).Equals(t1))))
            ||
            (equal && t2!.Equals(t1) && Equals(t1, t2) && t1.GetHashCode() == t2.GetHashCode()
             && (t1 is not IEquatable<T> e || (e.Equals(t2) && ((IEquatable<T>)t2).Equals(t1))));
        }, null, seed, iter, time, threads, print);
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
            throw new CsCheckException($"Hash is {fullHashCode}");
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
                throw new CsCheckException($"Actual {actualFullHash} but expected {expected}");
            }
        }
    }

    public static BigO BigO<T>(int[] n, Func<int, Gen<T>> genN, Action<T> action, long iter = -1, int repeat = 1, double constantFactor = 0.1)
    {
        if (iter == -1) iter = Iter;
        var gens = Array.ConvertAll(n, i => genN(i));
        var y = new MedianEstimator[n.Length];
        for (int i = 0; i < y.Length; i++)
            y[i] = new MedianEstimator();
        var timer = Timer.Create(action, repeat);
        var pcg = PCG.ThreadPCG;
        while (iter-- > 0)
            for (int i = 0; i < n.Length; i++)
                y[i].Add(timer.Time(gens[i].Generate(pcg, null, out _)));
        return BigO(Array.ConvertAll(n, i => (double)i), Array.ConvertAll(y, m => m.Median), constantFactor);
    }
}

internal sealed class FasterResult(double sigma, int repeat)
{
    readonly double Limit = sigma * sigma;
    public Exception? Exception;
    public int Faster, Slower;
    public long FasterMin = long.MaxValue, SlowerMin = long.MaxValue;
    public MedianEstimator Median = new();
    bool completed;

    private float SigmaSquared
    {
        // Binomial distribution: Mean = n p, Variance = n p q in this case H0 has n = Faster + Slower, p = 0.5, and q = 0.5
        // sigmas = Abs(Faster - Mean) / Sqrt(Variance) = Sqrt((Faster - Slower)^2/(Faster + Slower))
        get
        {
            float d = Faster - Slower;
            return d * d / (Faster + Slower);
        }
    }

    public bool NotFaster => Slower > Faster || Median.Median < 0.0;

    public bool Add(long faster, long slower)
    {
        lock (Median)
        {
            if (completed) return false;
            if (faster < FasterMin) FasterMin = faster;
            if (slower < SlowerMin) SlowerMin = slower;
            double ratio;
            if (slower > faster)
            {
                ratio = (double)(slower - faster) / slower;
                Faster++;
            }
            else if (slower != faster)
            {
                ratio = (double)(slower - faster) / faster;
                Slower++;
            }
            else
            {
                ratio = 0d;
            }
            Median.Add(ratio);
            if (SigmaSquared < Limit) return false;
            completed = true;
            return true;
        }
    }

    public override string ToString()
    {
        var times = Median.Median >= 0.0 ? 1 / (1 - Median.Median) : 1 + Median.Median;
        var q1Times = Median.Q1 >= 0.0 ? 1 / (1 - Median.Q1) : 1 + Median.Q1;
        var q3Times = Median.Q3 >= 0.0 ? 1 / (1 - Median.Q3) : 1 + Median.Q3;
        var faster = Median.Median >= 0.0 ? "faster" : "slower";
        if (Median.Median < 0.0)
        {
            times = 1 / times;
            (q1Times, q3Times) = (1 / q3Times, 1 / q1Times);
        }
        var (timeString, timeUnit) = TimeFormat((double)Math.Min(FasterMin, SlowerMin) / repeat);
        var result = $"{Median.Median:P2}[{Median.Q1:P2}..{Median.Q3:P2}] {times:#0.00}x[{q1Times:#0.00}x..{q3Times:#0.00}x] {faster}";
        if (double.IsNaN(Median.Median)) result = $"Time resolution too small try using repeat.\n{result}";
        else if ((Median.Median >= 0.0) != (Faster > Slower)) result = $"Inconsistent result try using repeat or increasing sigma.\n{result}";
        result = $"{result}, sigma = {Math.Sqrt(SigmaSquared):#0.0} ({Faster:#,0} vs {Slower:#,0}), min = {timeString((double)FasterMin / repeat)}{timeUnit} vs {timeString((double)SlowerMin / repeat)}{timeUnit}";
        if (Check.IsDebug) result += " - DEBUG MODE - DO NOT TRUST THESE RESULTS";
        return result;
    }

    private static (Func<double, string>, string) TimeFormat(double maxValue) =>
        (maxValue * 1000 / Stopwatch.Frequency) switch
        {
            >= 1000000 => (d => (d / Stopwatch.Frequency).ToString("###0"), "s"),
            >= 100000 => (d => (d / Stopwatch.Frequency).ToString("###0.#"), "s"),
            >= 10000 => (d => (d / Stopwatch.Frequency).ToString("###0.##"), "s"),
            >= 1000 => (d => (d * 1000 / Stopwatch.Frequency).ToString("###0"), "ms"),
            >= 100 => (d => (d * 1000 / Stopwatch.Frequency).ToString("###0.#"), "ms"),
            >= 10 => (d => (d * 1000 / Stopwatch.Frequency).ToString("###0.##"), "ms"),
            >= 1 => (d => (d * 1000 / Stopwatch.Frequency).ToString("###0.###"), "ms"),
            >= 0.1 => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("###0.#"), "μs"),
            >= 0.01 => (d => (d * 1_000_000 / Stopwatch.Frequency).ToString("###0.##"), "μs"),
            >= 0.001 => (d => (d * 1_000_000_000 / Stopwatch.Frequency).ToString("###0"), "ns"),
            >= 0.0001 => (d => (d * 1_000_000_000 / Stopwatch.Frequency).ToString("###0.#"), "ns"),
            >= 0.00001 => (d => (d * 1_000_000_000 / Stopwatch.Frequency).ToString("###0.##"), "ns"),
            >= 0.000001 => (d => (d * 1_000_000_000 / Stopwatch.Frequency).ToString("###0.###"), "ns"),
            _ => (d => (d * 1_000_000_000 / Stopwatch.Frequency).ToString("###0.####"), "ns"),
        };

    public void Output(Action<string> output) => output(ToString());
}