namespace CsCheck;

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#pragma warning disable IDE0059 // Unnecessary assignment of a value

public interface ITimerAction { long Time(); }
public interface ITimerAction<T> { long Time(T t); }
public interface ITimerFunc<R> { long Time(out R r); }
public interface ITimerFunc<T, R> { long Time(T t, out R r); }
public interface ITimerTaskAction { Task<long> Time(); }
public interface ITimerTaskAction<T> { Task<long> Time(T t); }
public interface ITimerTaskFunc<R> { Task<(long, R)> Time(); }
public interface ITimerTaskFunc<T, R> { Task<(long, R)> Time(T t); }
public interface IInvoke { void Invoke(); }
public interface IInvoke<T, R> { R Invoke(T t); }

public static class Timer
{
    public static ITimerAction Create(Action call, int count)
        => count == 1 ? new TimerActionOne(call)
         : count == 10 ? new TimerActionTen(call)
         : count % 10 == 0 ? new TimerActionManyTen(call, count / 10)
         : new TimerActionMany(call, count);
    public static ITimerAction<T> Create<T>(Action<T> call, int count)
        => count == 1 ? new TimerActionOne<T>(call)
         : count == 10 ? new TimerActionTen<T>(call)
         : count % 10 == 0 ? new TimerActionManyTen<T>(call, count / 10)
         : new TimerActionMany<T>(call, count);
    public static ITimerFunc<R> Create<R>(Func<R> call, int count)
        => count == 1 ? new TimerFuncOne<R>(call)
         : count == 10 ? new TimerFuncTen<R>(call)
         : count % 10 == 0 ? new TimerFuncManyTen<R>(call, count / 10)
         : new TimerFuncMany<R>(call, count);
    public static ITimerFunc<T, R> Create<T, R>(Func<T, R> call, int count)
        => count == 1 ? new TimerFuncOne<T, R>(call)
         : count == 10 ? new TimerFuncTen<T, R>(call)
         : count % 10 == 0 ? new TimerFuncManyTen<T, R>(call, count / 10)
         : new TimerFuncMany<T, R>(call, count);
    public static ITimerTaskAction Create(Func<Task> call, int count)
        => count == 1 ? new TimerTaskActionOne(call)
         : count == 10 ? new TimerTaskActionTen(call)
         : count % 10 == 0 ? new TimerTaskActionManyTen(call, count / 10)
         : new TimerTaskActionMany(call, count);
    public static ITimerTaskAction<T> Create<T>(Func<T, Task> call, int count)
        => count == 1 ? new TimerTaskActionOne<T>(call)
         : count == 10 ? new TimerTaskActionTen<T>(call)
         : count % 10 == 0 ? new TimerTaskActionManyTen<T>(call, count / 10)
         : new TimerTaskActionMany<T>(call, count);
    public static ITimerTaskFunc<R> Create<R>(Func<Task<R>> call, int count)
        => count == 1 ? new TimerTaskFuncOne<R>(call)
         : count == 10 ? new TimerTaskFuncTen<R>(call)
         : count % 10 == 0 ? new TimerTaskFuncManyTen<R>(call, count / 10)
         : new TimerTaskFuncMany<R>(call, count);
    public static ITimerTaskFunc<T, R> Create<T, R>(Func<T, Task<R>> call, int count)
        => count == 1 ? new TimerTaskFuncOne<T, R>(call)
         : count == 10 ? new TimerTaskFuncTen<T, R>(call)
         : count % 10 == 0 ? new TimerTaskFuncManyTen<T, R>(call, count / 10)
         : new TimerTaskFuncMany<T, R>(call, count);
    public static ITimerAction Create<I>(I call, int count) where I : IInvoke
        => count == 1 ? new TimerInvokeOne<I>(call)
         : count == 10 ? new TimerInvokeTen<I>(call)
         : count == 100 ? new TimerInvoke100<I>(call)
         : count % 10 == 0 ? new TimerInvokeManyTen<I>(call, count / 10)
         : new TimerInvokeMany<I>(call, count);
    public static ITimerFunc<T, R> Create<I, T, R>(I call, int count) where I : IInvoke<T, R>
        => count == 1 ? new TimerInvokeOne<I, T, R>(call)
         : count == 10 ? new TimerInvokeTen<I, T, R>(call)
         : count == 100 ? new TimerInvoke100<I, T, R>(call)
         : count % 10 == 0 ? new TimerInvokeManyTen<I, T, R>(call, count / 10)
         : new TimerInvokeMany<I, T, R>(call, count);
    sealed class TimerActionOne(Action call) : ITimerAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionTen(Action call) : ITimerAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            call();
            call();
            call();
            call();
            call();
            call();
            call();
            call();
            call();
            call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionManyTen(Action call, int count) : ITimerAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                call();
                call();
                call();
                call();
                call();
                call();
                call();
                call();
                call();
                call();
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionMany(Action call, int count) : ITimerAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionOne<T>(Action<T> call) : ITimerAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionTen<T>(Action<T> call) : ITimerAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionManyTen<T>(Action<T> call, int count) : ITimerAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
                call(t);
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerActionMany<T>(Action<T> call, int count) : ITimerAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncOne<R>(Func<R> call) : ITimerFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncTen<R>(Func<R> call) : ITimerFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            r = call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncManyTen<R>(Func<R> call, int count) : ITimerFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
                r = call();
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncMany<R>(Func<R> call, int count) : ITimerFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                r = call();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncOne<T, R>(Func<T, R> call) : ITimerFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncTen<T, R>(Func<T, R> call) : ITimerFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            r = call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncManyTen<T, R>(Func<T, R> call, int count) : ITimerFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
                r = call(t);
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerFuncMany<T, R>(Func<T, R> call, int count) : ITimerFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                r = call(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionOne(Func<Task> call) : ITimerTaskAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time()
        {
            var start = Stopwatch.GetTimestamp();
            await call().ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionTen(Func<Task> call) : ITimerTaskAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time()
        {
            var start = Stopwatch.GetTimestamp();
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            await call().ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionManyTen(Func<Task> call, int count) : ITimerTaskAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
                await call().ConfigureAwait(false);
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionMany(Func<Task> call, int count) : ITimerTaskAction
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                await call().ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionOne<T>(Func<T, Task> call) : ITimerTaskAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            await call(t).ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionTen<T>(Func<T, Task> call) : ITimerTaskAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            await call(t).ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionManyTen<T>(Func<T, Task> call, int count) : ITimerTaskAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
                await call(t).ConfigureAwait(false);
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskActionMany<T>(Func<T, Task> call, int count) : ITimerTaskAction<T>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<long> Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                await call(t).ConfigureAwait(false);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerTaskFuncOne<R>(Func<Task<R>> call) : ITimerTaskFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time()
        {
            var start = Stopwatch.GetTimestamp();
            var r = await call().ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncTen<R>(Func<Task<R>> call) : ITimerTaskFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time()
        {
            R r = default!;
            var start = Stopwatch.GetTimestamp();
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            r = await call().ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncManyTen<R>(Func<Task<R>> call, int count) : ITimerTaskFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time()
        {
            R r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
                r = await call().ConfigureAwait(false);
            }
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncMany<R>(Func<Task<R>> call, int count) : ITimerTaskFunc<R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time()
        {
            R r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                r = await call().ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncOne<T, R>(Func<T, Task<R>> call) : ITimerTaskFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time(T t)
        {
            var start = Stopwatch.GetTimestamp();
            var r = await call(t).ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncTen<T, R>(Func<T, Task<R>> call) : ITimerTaskFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time(T t)
        {
            R r;
            var start = Stopwatch.GetTimestamp();
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            r = await call(t).ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncManyTen<T, R>(Func<T, Task<R>> call, int count) : ITimerTaskFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time(T t)
        {
            R r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
                r = await call(t).ConfigureAwait(false);
            }
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerTaskFuncMany<T, R>(Func<T, Task<R>> call, int count) : ITimerTaskFunc<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public async Task<(long, R)> Time(T t)
        {
            R r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                r = await call(t).ConfigureAwait(false);
            return (Stopwatch.GetTimestamp() - start, r);
        }
    }
    sealed class TimerInvokeOne<I>(I call) : ITimerAction where I : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            call.Invoke();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeTen<I>(I call) : ITimerAction where I : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvoke100<I>(I call) : ITimerAction where I : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            call.Invoke();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeManyTen<I>(I call, int count) : ITimerAction where I : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
                call.Invoke();
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeMany<I>(I call, int count) : ITimerAction where I : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time()
        {
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                call.Invoke();
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeOne<I, T, R>(I call) : ITimerFunc<T, R> where I : IInvoke<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call.Invoke(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeTen<I, T, R>(I call) : ITimerFunc<T, R> where I : IInvoke<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvoke100<I, T, R>(I call) : ITimerFunc<T, R> where I : IInvoke<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            var start = Stopwatch.GetTimestamp();
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            r = call.Invoke(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeManyTen<I, T, R>(I call, int count) : ITimerFunc<T, R> where I : IInvoke<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
                r = call.Invoke(t);
            }
            return Stopwatch.GetTimestamp() - start;
        }
    }
    sealed class TimerInvokeMany<I, T, R>(I call, int count) : ITimerFunc<T, R> where I : IInvoke<T, R>
    {
        [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
        public long Time(T t, out R r)
        {
            r = default!;
            var start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
                r = call.Invoke(t);
            return Stopwatch.GetTimestamp() - start;
        }
    }
}