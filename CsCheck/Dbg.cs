using System;
using System.Linq;
using System.Collections.Generic;
using CsCheck;

public static class Dbg
{
    static readonly List<string> info = new();
    static readonly Dictionary<string, int> count = new();
    static readonly Dictionary<string, Action> function = new();

    public static void Output(Action<string> output)
    {
        foreach (var s in info)
            output(string.Concat("Dbg: ", s));
        int maxLength = 0, totalCount = 0;
        foreach (var kv in count)
        {
            totalCount += kv.Value;
            if (kv.Key.Length > maxLength)
                maxLength = kv.Key.Length;
        }
        foreach (var kc in count.OrderByDescending(i => i.Value))
        {
            var percent = ((float)kc.Value / totalCount).ToString("0.0%").PadLeft(7);
            output(string.Concat(kc.Key.PadRight(maxLength), percent, " ", kc.Value));
        }
        Clear();
    }

    public static void Clear()
    {
        info.Clear();
        count.Clear();
        function.Clear();
    }

    public static void Add(string s)
    {
        lock (info)
        {
            info.Add(s);
        }
    }

    public static void Count<T>(T t)
    {
        var s = Check.Print(t);
        lock (count)
        {
            count.TryGetValue(s, out int c);
            count[s] = c + 1;
        }
    }

    public static IEnumerable<T> Debug<T>(this IEnumerable<T> source, string name)
    {
        foreach (var t in source)
        {
            Add(string.Concat(name, " : ", Check.Print(t)));
            yield return t;
        }
    }

    public static Gen<T> DebugClassify<T>(this Gen<T> gen, Func<T, string> name) =>
        Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        var t = gen.Generate(pcg, min, out size);
        Count(name(t));
        return t;
    });

    public static void Add<T>(string name, Action<T> f, T t)
    {
        Add(string.Concat(name, " : ", Check.Print(t)));
        f(t);
    }

    public static void Add<T1, T2>(string name, Action<T1, T2> f, T1 t1, T2 t2)
    {
        Add(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2)));
        f(t1, t2);
    }

    public static void Add<T1, T2, T3>(string name, Action<T1, T2, T3> f, T1 t1, T2 t2, T3 t3)
    {
        Add(string.Concat(name, " : ", Check.Print(t1), ", ", Check.Print(t2), ", ", Check.Print(t3)));
        f(t1, t2, t3);
    }

    public static R Add<R>(string name, Func<R> f)
    {
        var r = f();
        Add(string.Concat(name, " : ", Check.Print(r)));
        return r;
    }

    public static R Add<T, R>(string name, Func<T, R> f, T t)
    {
        var r = f(t);
        Add(string.Concat(name, " : ", Check.Print(t), " -> ", Check.Print(r)));
        return r;
    }

    public static R Add<T1, T2, R>(string name, Func<T1, T2, R> f, T1 t1, T2 t2)
    {
        var r = f(t1, t2);
        Add(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(r)));
        return r;
    }

    public static R Add<T1, T2, T3, R>(string name, Func<T1, T2, T3, R> f, T1 t1, T2 t2, T3 t3)
    {
        var r = f(t1, t2, t3);
        Add(string.Concat(name, " : ", Check.Print(t1), " -> ", Check.Print(t2), " -> ", Check.Print(t3), " -> ", Check.Print(r)));
        return r;
    }

    public static void Function(string name, Action action)
    {
        lock (function)
        {
            function[name] = action;
        }
    }

    public static void Function(string name) => function[name]();
}