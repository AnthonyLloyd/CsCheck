// Copyright 2023 Anthony Lloyd
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class CsCheckException : Exception
{
    private CsCheckException() {}
    public CsCheckException(string message) : base(message) { }
    public CsCheckException(string message, Exception? exception) : base(message, exception) { }
}

/// <summary>Main random testing Check functions.</summary>
public static partial class Check
{
#pragma warning disable CA2211 // Non-constant fields should not be visible
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
    public static string? Seed;
    /// <summary>The sigma to use for Faster (default 6).</summary>
    public static double Sigma = 6.0;
    /// <summary>The timeout in seconds to use for Faster (default 60 seconds).</summary>
    public static int Timeout = 60;
    /// <summary>The number of ulps to approximate to when printing doubles and floats.</summary>
    public static int Ulps = 4;
    /// <summary>The number of Where Gne iterations before throwing an exception.</summary>
    public static int WhereLimit = 100;
#pragma warning restore CA2211 // Non-constant fields should not be visible

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
        var timeout = Environment.GetEnvironmentVariable("CsCheck_Timeout");
        if (!string.IsNullOrWhiteSpace(timeout)) Timeout = int.Parse(timeout);
        var ulps = Environment.GetEnvironmentVariable("CsCheck_Ulps");
        if (!string.IsNullOrWhiteSpace(ulps)) Ulps = int.Parse(ulps);
        var whereLimit = Environment.GetEnvironmentVariable("CsCheck_WhereLimit");
        if (!string.IsNullOrWhiteSpace(whereLimit)) WhereLimit = int.Parse(whereLimit);
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;

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
                assert(t = gen.Generate(pcg, null, out size));
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
        var cde = new CountdownEvent(threads);
        void Worker(object? _)
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
                            cde.Signal();
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, minSize, out size);
                        if (minSize is null || Size.IsLessThan(size, minSize))
                            assert(t);
                        else
                            skippedLocal++;
                    }
                }
                catch (Exception e)
                {
                    lock (cde)
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
        }
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(Worker, null);
        Worker(null);
        cde.Wait();
        if (minPCG is not null)
        {
            var seedString = minPCG.ToString(minState);
            var tString = print(minT!);
            if (tString.Length > MAX_LENGTH) tString = string.Concat(tString.AsSpan(0, MAX_LENGTH), " ...");
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Action<T1, T2, T3, T4, T5, T6, T7, T8> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null)
        => Sample(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T>(this Gen<T> gen, Func<T, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T t)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3, T4 t4)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6, t7);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data retuning a classification and raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, string> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        void action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8)
        {
            var time = Stopwatch.GetTimestamp();
            var name = classify(t1, t2, t3, t4, t5, t6, t7, t8);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        Sample(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;

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
                await assert(t = gen.Generate(pcg, null, out size));
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
        while (threads-- > 0)
        {
            tasks[threads] = Task.Run(async () =>
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
                                await assert(t);
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
            });
        }

        await Task.WhenAll(tasks);
        if (minPCG is not null)
        {
            var seedString = minPCG.ToString(minState);
            var tString = print(minT!);
            if (tString.Length > MAX_LENGTH) tString = string.Concat(tString.AsSpan(0, MAX_LENGTH), " ...");
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
    public static Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the assert each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="assert">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> assert,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null)
        => SampleAsync(gen, t => assert(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T t)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3, T4 t4)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6, t7);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the classify each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="classify">The code to call with the input data raising an exception if it fails.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    /// <param name="writeLine">WriteLine function to use for the classify summary output.</param>
    public static async Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<string>> classify,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null,
        Action<string>? writeLine = null)
    {
        var classifier = new Classifier();
        async Task action(T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8)
        {
            var time = Stopwatch.GetTimestamp();
            var name = await classify(t1, t2, t3, t4, t5, t6, t7, t8);
            classifier.Add(name, Stopwatch.GetTimestamp() - time);
        }
        await SampleAsync(gen, action, seed, iter, time, threads, print);
        classifier.Print(writeLine);
    }

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static void Sample<T>(this Gen<T> gen, Func<T, bool> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;

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
                if (!predicate(t))
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
        var cde = new CountdownEvent(threads);
        void Worker(object? _)
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
                            cde.Signal();
                            return;
                        }
                        totalLocal++;
                        state = pcg.State;
                        t = gen.Generate(pcg, minSize, out size);
                        if (minSize is null || Size.IsLessThan(size, minSize))
                        {
                            if (!predicate(t))
                            {
                                lock (cde)
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
                    lock (cde)
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
        }
        while (--threads > 0)
            ThreadPool.UnsafeQueueUserWorkItem(Worker, null);
        Worker(null);
        cde.Wait();
        if (minPCG is not null)
        {
            var seedString = minPCG.ToString(minState);
            var tString = print(minT!);
            if (tString.Length > MAX_LENGTH) tString = string.Concat(tString.AsSpan(0, MAX_LENGTH), " ...");
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null)
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
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static void Sample<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null)
        => Sample(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static async Task SampleAsync<T>(this Gen<T> gen, Func<T, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null)
    {
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;

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
                if (!await predicate(t))
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
        while (threads-- > 0)
        {
            tasks[threads] = Task.Run(async () =>
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
                                if (!await predicate(t))
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
            });
        }

        await Task.WhenAll(tasks);
        if (minPCG is not null)
        {
            var seedString = minPCG.ToString(minState);
            var tString = print(minT!);
            if (tString.Length > MAX_LENGTH) tString = string.Concat(tString.AsSpan(0, MAX_LENGTH), " ...");
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
    public static Task SampleAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), seed, iter, time, threads, print);

    /// <summary>Sample the gen calling the predicate each time across multiple threads. Shrink any exceptions if necessary.</summary>
    /// <param name="gen">The sample input data generator.</param>
    /// <param name="predicate">The code to call with the input data returning if it is successful.</param>
    /// <param name="seed">The initial seed to use for the first iteration.</param>
    /// <param name="iter">The number of iterations to run in the sample (default 100).</param>
    /// <param name="time">The number of seconds to run the sample.</param>
    /// <param name="threads">The number of threads to run the sample on (default number logical CPUs).</param>
    /// <param name="print">A function to convert the input data to a string for error reporting (default Check.Print).</param>
    public static Task SampleAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<bool>> predicate,
        string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<(T1, T2, T3, T4, T5, T6, T7, T8), string>? print = null)
        => SampleAsync(gen, t => predicate(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), seed, iter, time, threads, print);

    sealed class ModelBasedData<Actual, Model>(Actual actualState, Model modelState, uint stream, ulong seed, (string, Action<Actual, Model>)[] operations)
    {
        public Actual ActualState = actualState; public Model ModelState = modelState; public uint Stream = stream; public ulong Seed = seed; public (string, Action<Actual, Model>)[] Operations = operations; public Exception? Exception;
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
    public static void SampleModelBased<Actual, Model>(this Gen<(Actual, Model)> initial, GenOperation<Actual, Model>[] operations,
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
    {
        equal ??= ModelEqual;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        printActual ??= Print;
        printModel ??= Print;

        var opNameActions = new Gen<(string, Action<Actual, Model>)>[operations.Length];
        for (int i = 0; i < operations.Length; i++)
        {
            var op = operations[i];
            var opName = "Op" + i;
            opNameActions[i] = op.AddOpNumber ? op.Select(t => (opName + t.Item1, t.Item2)) : op;
        }
        
        new GenInitial<Actual, Model>(initial)
        .Select(Gen.OneOf(opNameActions).Array, (a, b) => new ModelBasedData<Actual, Model>(a.Actual, a.Model, a.Stream, a.Seed, b))
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
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
        Func<Actual, Model, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<Actual, string>? printActual = null, Func<Model, string>? printModel = null)
        => SampleModelBased(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
            equal, seed, iter, time, threads, printActual, printModel);

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
    public static void SampleMetamorphic<T>(this Gen<T> initial, GenMetamorphic<T> operations,
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1,
        Func<T, string>? print = null)
    {
        equal ??= ModelEqual;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;

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
        }, seed, iter, time, threads,
        p =>
        {
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
                sb.Append("\n    Exception: ").Append(p.Item1.Exception.ToString());
            }
            return sb.ToString();
        });
    }

    sealed class ConcurrentData<T>(T state, uint stream, ulong seed, (string, Action<T>)[] operations, int threads)
    {
        public T State = state; public uint Stream = stream; public ulong Seed = seed; public (string, Action<T>)[] Operations = operations; public int Threads = threads; public int[]? ThreadIds; public Exception? Exception;
    }

    internal const int MAX_CONCURRENT_OPERATIONS = 10;

    sealed class GenConcurrent<T>(Gen<T> initial) : Gen<(T Value, uint Stream, ulong Seed)>
    {
        public override (T, uint, ulong) Generate(PCG pcg, Size? min, out Size size)
        {
            var stream = pcg.Stream;
            var seed = pcg.Seed;
            return (initial.Generate(pcg, null, out size), stream, seed);
        }
    }

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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
    {
        equal ??= Equal;
        seed ??= Seed;
        if (iter == -1) iter = Iter;
        if (time == -1) time = Time;
        if (threads == -1) threads = Threads;
        print ??= Print;
        if (replay == -1) replay = Replay;
        int[]? replayThreads = null;
        if (seed?.Contains('[') == true)
        {
            int i = seed.IndexOf('[');
            int j = seed.IndexOf(']', i + 1);
            replayThreads = seed.Substring(i + 1, j - i - 1).Split(',').Select(int.Parse).ToArray();
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

        new GenConcurrent<T>(initial)
        .Select(Gen.OneOf(opNameActions).Array[1, MAX_CONCURRENT_OPERATIONS]
        .SelectMany(ops => Gen.Int[1, Math.Min(threads, ops.Length)].Select(i => (ops, i))), (a, b) =>
            new ConcurrentData<T>(a.Value, a.Stream, a.Seed, b.ops, b.i)
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
                Parallel.ForEach(Permutations(cd.ThreadIds, cd.Operations), (sequence, state) =>
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
            } while (linearizable && firstIteration && seed is not null && --replay > 0);
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
            foreach (var sequence in Permutations(p.ThreadIds!, p.Operations))
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
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
        Func<T, T, bool>? equal = null, string? seed = null, long iter = -1, int time = -1, int threads = -1, Func<T, string>? print = null, int replay = -1)
        => SampleConcurrent(initial, new[] { operation1, operation2, operation3, operation4, operation5, operation6 },
            equal, seed, iter, time, threads, print, replay);

    /// <summary>Assert actual is in line with expected using a chi-squared test to 6 sigma.</summary>
    /// <param name="expected">The expected bin counts.</param>
    /// <param name="actual">The actual bin counts.</param>
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
        if (sigmaSquared > 36.0) throw new CsCheckException("Chi-squared standard deviation = " + Math.Sqrt(sigmaSquared).ToString("0.0"));
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
                        if (raiseexception && result.NotFaster)
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
    public static FasterResult Faster(Action faster, Action slower, double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
        return result.Exception is null ? result : throw result.Exception;
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
                        if (raiseexception && result.NotFaster)
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
    public static FasterResult Faster<T>(Func<T> faster, Func<T> slower, Func<T, T, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
        if (raiseexception && result.NotFaster)
            throw new CsCheckException(result.ToString());
        return result.Exception is null ? result : throw result.Exception;
    }

    /// <summary>Assert the first function is faster than the second to a given sigma.</summary>
    /// <param name="faster">The presumed faster code to test.</param>
    /// <param name="slower">The presumed slower code to test.</param>
    /// <param name="sigma">The sigma is the number of standard deviations from the null hypothesis (default 6).</param>
    /// <param name="threads">The number of threads to run the code on (default number logical CPUs).</param>
    /// <param name="repeat">The number of times to call each of the actions in each iteration if they are too quick to accurately measure (default 1).</param>
    /// <param name="timeout">The number of seconds to wait before timing out (default 60). </param>
    /// <param name="raiseexception">If set an exception will be raised with statistics if slower is actually the fastest (default true).</param>
    public static async Task<FasterResult> FasterAsync(Func<Task> faster, Func<Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true)
    {
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var running = true;
        async Task Worker()
        {
            try
            {
                while (running)
                {
                    if (result.Add(await fasterTimer.Time(), await slowerTimer.Time()))
                    {
                        if (raiseexception && result.NotFaster)
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
        await Worker();
        return result.Exception is null ? result : throw result.Exception;
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
    public static async Task<FasterResult> FasterAsync<T>(Func<Task<T>> faster, Func<Task<T>> slower, Func<T, T, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, bool raiseexception = true)
    {
        equal ??= Equal;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var running = true;
        async Task Worker()
        {
            try
            {
                while (running)
                {
                    var (fasterTime, fasterValue) = await fasterTimer.Time();
                    var (slowerTime, slowerValue) = await slowerTimer.Time();
                    if (result.Add(fasterTime, slowerTime))
                    {
                        if (raiseexception && result.NotFaster)
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
        await Worker();
        return result.Exception is null ? result : throw result.Exception;
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
                        if (raiseexception && result.NotFaster)
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
                if (tString.Length > 100) tString = tString[..100];
                result.Exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tString}", e);
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
    public static FasterResult Faster<T>(this Gen<T> gen, Action<T> faster, Action<T> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
        return result.Exception is null ? result : throw result.Exception;
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
    public static FasterResult Faster<T1, T2>(this Gen<(T1, T2)> gen, Action<T1, T2> faster, Action<T1, T2> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Action<T1, T2, T3> faster, Action<T1, T2, T3> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Action<T1, T2, T3, T4> faster, Action<T1, T2, T3, T4> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Action<T1, T2, T3, T4, T5> faster, Action<T1, T2, T3, T4, T5> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Action<T1, T2, T3, T4, T5, T6> faster, Action<T1, T2, T3, T4, T5, T6> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Action<T1, T2, T3, T4, T5, T6, T7> faster, Action<T1, T2, T3, T4, T5, T6, T7> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Action<T1, T2, T3, T4, T5, T6, T7, T8> faster, Action<T1, T2, T3, T4, T5, T6, T7, T8> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static async Task<FasterResult> FasterAsync<T>(this Gen<T> gen, Func<T, Task> faster, Func<T, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
    {
        seed ??= Seed;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
                    if (result.Add(await fasterTimer.Time(t), await slowerTimer.Time(t)))
                    {
                        if (raiseexception && result.NotFaster)
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
                if (tString.Length > 100) tString = tString[..100];
                result.Exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tString}", e);
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker();
        return result.Exception is null ? result : throw result.Exception;
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
    public static Task<FasterResult> FasterAsync<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, Task> faster, Func<T1, T2, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task> faster, Func<T1, T2, T3, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task> faster, Func<T1, T2, T3, T4, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task> faster, Func<T1, T2, T3, T4, T5, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task> faster, Func<T1, T2, T3, T4, T5, T6, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task> faster, Func<T1, T2, T3, T4, T5, T6, T7, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> slower,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), sigma, threads, repeat, timeout, seed, raiseexception);

    sealed class FasterFuncWorker<T, R>(Gen<T> gen, ITimerFunc<T,R> fasterTimer, ITimerFunc<T, R> slowerTimer, FasterResult result, long endTimestamp, Func<R, R, bool> equal, string? seed, bool raiseexception) : IThreadPoolWorkItem
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
                        if (raiseexception && result.NotFaster)
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
                        result.Exception ??= new CsCheckException($"Return values differ: CsCheck_Seed = \"{pcg.ToString(state)}\"{vfs}{vss}");
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
                if (tString.Length > 100) tString = tString[..100];
                result.Exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tString}", e);
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
    public static FasterResult Faster<T, R>(this Gen<T> gen, Func<T, R> faster, Func<T, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
        return result.Exception is null ? result : throw result.Exception;
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
    public static FasterResult Faster<I1, I2, T, R>(this Gen<T> gen, I1 faster, I2 slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
            where I1 : IInvoke<T, R> where I2 : IInvoke<T, R>
    {
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
        return result.Exception is null ? result : throw result.Exception;
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
    public static FasterResult Faster<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, R> faster, Func<T1, T2, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> faster, Func<T1, T2, T3, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> faster, Func<T1, T2, T3, T4, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> faster, Func<T1, T2, T3, T4, T5, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> faster, Func<T1, T2, T3, T4, T5, T6, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> faster, Func<T1, T2, T3, T4, T5, T6, T7, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static FasterResult Faster<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => Faster(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static async Task<FasterResult> FasterAsync<T, R>(this Gen<T> gen, Func<T, Task<R>> faster, Func<T, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
    {
        seed ??= Seed;
        equal ??= Equal;
        var fasterTimer = Timer.Create(faster, repeat);
        var slowerTimer = Timer.Create(slower, repeat);
        var endTimestamp = Stopwatch.GetTimestamp() + (timeout == -1 ? Timeout : timeout) * Stopwatch.Frequency;
        var result = new FasterResult(sigma == -1 ? Sigma : sigma);
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
                    var (fasterTime, fasterValue) = await fasterTimer.Time(t);
                    var (slowerTime, slowerValue) = await slowerTimer.Time(t);
                    if (result.Add(fasterTime, slowerTime))
                    {
                        if (raiseexception && result.NotFaster)
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
                    if (running && Stopwatch.GetTimestamp() > timeout)
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
                if (tString.Length > 100) tString = tString[..100];
                result.Exception = new CsCheckException($"CsCheck_Seed = \"{pcg.ToString(state)}\" T={tString}", e);
                running = false;
            }
        }
        if (threads == -1) threads = Threads;
        while (--threads > 0)
            _ = Task.Run(Worker);
        await Worker();
        return result.Exception is null ? result : throw result.Exception;
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
    public static Task<FasterResult> FasterAsync<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, Task<R>> faster, Func<T1, T2, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2), t => slower(t.Item1, t.Item2), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, Task<R>> faster, Func<T1, T2, T3, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3), t => slower(t.Item1, t.Item2, t.Item3), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, Task<R>> faster, Func<T1, T2, T3, T4, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4), t => slower(t.Item1, t.Item2, t.Item3, t.Item4), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, Task<R>> faster, Func<T1, T2, T3, T4, T5, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, T7, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7), equal, sigma, threads, repeat, timeout, seed, raiseexception);

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
    public static Task<FasterResult> FasterAsync<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<R>> faster, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task<R>> slower, Func<R, R, bool>? equal = null,
        double sigma = -1.0, int threads = -1, int repeat = 1, int timeout = -1, string? seed = null, bool raiseexception = true)
        => FasterAsync(gen, t => faster(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), t => slower(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8), equal, sigma, threads, repeat, timeout, seed, raiseexception);

    /// <summary>Generate a single random example.</summary>
    /// <param name="gen">The data generator.</param>
    public static T Single<T>(this Gen<T> gen)
        => gen.Generate(PCG.ThreadPCG, null, out _);

    sealed class SingleWorker<T>(Gen<T> gen, Func<T, bool> predicate) : IThreadPoolWorkItem
    {
        public volatile string? message = null;
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
        , seed, iter, time, threads, print);

        gen.Select(gen).Sample((t1, t2) =>
        {
            bool equal = t1!.Equals(t2);
            return
            (!equal && !t2!.Equals(t1) && !Equals(t1, t2)
             && (t1 is not IEquatable<T> e2 || (!e2.Equals(t2) && !((IEquatable<T>)t2).Equals(t1))))
            ||
            (equal && t2!.Equals(t1) && Equals(t1, t2) && t1.GetHashCode() == t2.GetHashCode()
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
}

public sealed class FasterResult(double sigma)
{
    const byte STATE_PROCESSING = 0, STATE_QUEUED = 1, STATE_BLOCKED = 2;
    static readonly int capacity = Environment.ProcessorCount;
    readonly double Limit = sigma * sigma;
    public Exception? Exception;
    public int Faster, Slower;
    public MedianEstimator Median = new();
    readonly Queue<double> queue = new(capacity);
    SpinLock spinLock = new();
    bool processing = false;

    public float SigmaSquared
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
        double ratio;
        byte myState;
        if (slower > faster)
        {
            ratio = ((double)(slower - faster)) / slower;
            bool lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (processing)
            {
                if (queue.Count == capacity)
                {
                    myState = STATE_BLOCKED;
                }
                else
                {
                    queue.Enqueue(ratio);
                    myState = STATE_QUEUED;
                }
            }
            else
            {
                processing = true;
                myState = STATE_PROCESSING;
            }
            Faster++;
            if (lockTaken) spinLock.Exit();
        }
        else if (slower < faster)
        {
            ratio = ((double)(slower - faster)) / faster;
            bool lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (processing)
            {
                if (queue.Count == capacity)
                {
                    myState = STATE_BLOCKED;
                }
                else
                {
                    queue.Enqueue(ratio);
                    myState = STATE_QUEUED;
                }
            }
            else
            {
                processing = true;
                myState = STATE_PROCESSING;
            }
            Slower++;
            if (lockTaken) spinLock.Exit();
        }
        else
        {
            ratio = 0d;
            bool lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (processing)
            {
                if (queue.Count == capacity)
                {
                    myState = STATE_BLOCKED;
                }
                else
                {
                    queue.Enqueue(ratio);
                    myState = STATE_QUEUED;
                }
            }
            else
            {
                processing = true;
                myState = STATE_PROCESSING;
            }
            if (lockTaken) spinLock.Exit();
        }
        if (myState == STATE_PROCESSING)
        {
            while (true)
            {
                Median.Add(ratio);
                bool lockTaken = false;
                spinLock.Enter(ref lockTaken);
                if (queue.Count == 0)
                {
                    processing = false;
                    if (lockTaken) spinLock.Exit();
                    break;
                }
                ratio = queue.Dequeue();
                if (lockTaken) spinLock.Exit();
            }
        }
        else if (myState == STATE_BLOCKED)
        {
            while (true)
            {
                bool lockTaken = false;
                spinLock.Enter(ref lockTaken);
                if (queue.Count != capacity)
                {
                    queue.Enqueue(ratio);
                    if (lockTaken) spinLock.Exit();
                    break;
                }
                if (lockTaken) spinLock.Exit();
            }
        }
        return SigmaSquared >= Limit;
    }
    public override string ToString()
    {
        while (true)
        {
            bool lockTaken = false;
            spinLock.Enter(ref lockTaken);
            if (!processing)
            {
                var result = $"{Median.Median * 100.0:#0.0}%[{Median.Q1 * 100.0:#0.0}%..{Median.Q3 * 100.0:#0.0}%] {(Median.Median >= 0.0 ? "faster" : "slower")}";
                if (double.IsNaN(Median.Median)) result = $"Time resolution too small try using repeat.\n{result}";
                else if ((Median.Median >= 0.0) != (Faster > Slower)) result = $"Inconsistent result try using repeat or increasing sigma.\n{result}";
                result = $"{result}, sigma={Math.Sqrt(SigmaSquared):#0.0} ({Faster:#,0} vs {Slower:#,0})";
                if (lockTaken) spinLock.Exit();
                return result;
            }
            if (lockTaken) spinLock.Exit();
        }
    }
    public void Output(Action<string> output) => output(ToString());
}