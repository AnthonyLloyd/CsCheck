﻿// Copyright 2025 Anthony Lloyd
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

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.VisualBasic;
using System.Collections.Generic;

/// <summary>Size representation of Gen generated data.</summary>
public sealed class Size
{
    public ulong I;
    public Size? Next;

    public Size(ulong i) => I = i;

    public Size(ulong i, Size next)
    {
        I = i;
        Next = next;
    }

    public void Add(Size a)
    {
        var nI = I + a.I;
        I = nI >= I && nI >= a.I ? nI : ulong.MaxValue;
        if (a.Next is not null)
        {
            if (Next is null) Next = a.Next;
            else Next.Add(a.Next);
        }
    }
    public void Append(Size s)
    {
        var final = this;
        while (final.Next is not null) final = final.Next;
        final.Next = s;
    }

    public Size? Below(Size? s)
    {
        var r = this;
        while (s is not null && r is not null)
        {
            if (s.I != r.I) return null;
            s = s.Next;
            r = r.Next;
        }
        return r;
    }

    public static bool IsLessThan(Size? s1, Size? s2)
    {
        while (true)
        {
            if (s1 is null || s2 is null || s1.I > s2.I) return false;
            if (s1.I < s2.I) return true;
            s1 = s1.Next;
            s2 = s2.Next;
        }
    }
}

public interface IGen<out T>
{
    T Generate(PCG pcg, Size? min, out Size size);
}

/// <summary>Data and Size generator from a PCG random number generator.</summary>
public abstract class Gen<T> : IGen<T>
{
    public abstract T Generate(PCG pcg, Size? min, out Size size);

    public GenOperation<S> Operation<S>(Func<T, string> name, Action<S, T> action) => GenOperation.Create(this, name, action);
    public GenOperation<S> Operation<S>(Func<T, string> name, Func<S, T, Task> async) => GenOperation.Create(this, name, (S s, T t) => async(s, t).GetAwaiter().GetResult());
    public GenOperation<S> Operation<S>(Action<S, T> action) => GenOperation.Create(this, action);
    public GenOperation<S> Operation<S>(Func<S, T, Task> async) => GenOperation.Create(this, (S s, T t) => async(s, t).GetAwaiter().GetResult());
    public GenOperation<Actual, Model> Operation<Actual, Model>(Func<T, string> name, Action<Actual, T> actual, Action<Model, T> model) => GenOperation.Create(this, name, actual, model);
    public GenOperation<Actual, Model> Operation<Actual, Model>(Action<Actual, T> actual, Action<Model, T> model) => GenOperation.Create(this, actual, model);
    public GenMetamorphic<S> Metamorphic<S>(Func<T, string> name, Action<S, T> action1, Action<S, T> action2) => GenMetamorphic.Create(this, name, action1, action2);
    public GenMetamorphic<S> Metamorphic<S>(Action<S, T> action1, Action<S, T> action2) => GenMetamorphic.Create(this, Check.Print, action1, action2);

    /// <summary>Generator for an array of <typeparamref name="T"/></summary>
    public GenArray<T> Array => new(this);
    /// <summary>Generator for a two dimensional array of <typeparamref name="T"/></summary>
    public GenArray2D<T> Array2D => new(this);
    /// <summary>Generator for a List of <typeparamref name="T"/></summary>
    public GenList<T> List => new(this);
    /// <summary>Generator for a HashSet of <typeparamref name="T"/></summary>
    public GenHashSet<T> HashSet => new(this);
    /// <summary>Generator for a unique array of <typeparamref name="T"/></summary>
    public GenArrayUnique<T> ArrayUnique => new(this);
}

public delegate T GenMap<T>(T v, ref Size size);

/// <summary>Provides a set of static methods for composing generators.</summary>
public static class Gen
{
    sealed class GenConst<T>(T value) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            size = new Size(0);
            return value;
        }
    }
    /// <summary>Create a constant generator.</summary>
    public static Gen<T> Const<T>(T value) => new GenConst<T>(value);

    sealed class GenConstFunc<T>(Func<T> value) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            size = new Size(0);
            return value();
        }
    }
    /// <summary>Create a constant generator.</summary>
    public static Gen<T> Const<T>(Func<T> value) => new GenConstFunc<T>(value);

    sealed class GenSelect<T, R>(Gen<T> gen, Func<T, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var t = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T, R>(this Gen<T> gen, Func<T, R> selector)
        => new GenSelect<T, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, R>(Gen<(T1, T2)> gen, Func<T1, T2, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, R> selector)
        => new GenSelectTuple<T1, T2, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, R>(Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> selector)
        => new GenSelectTuple<T1, T2, T3, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, T4, R>(Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3, t4) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3, t4);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> selector)
        => new GenSelectTuple<T1, T2, T3, T4, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, T4, T5, R>(Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3, t4, t5) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3, t4, t5);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> selector)
        => new GenSelectTuple<T1, T2, T3, T4, T5, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, T4, T5, T6, R>(Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3, t4, t5, t6) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3, t4, t5, t6);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> selector)
        => new GenSelectTuple<T1, T2, T3, T4, T5, T6, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, T4, T5, T6, T7, R>(Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3, t4, t5, t6, t7) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3, t4, t5, t6, t7);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> selector)
        => new GenSelectTuple<T1, T2, T3, T4, T5, T6, T7, R>(gen, selector);

    sealed class GenSelectTuple<T1, T2, T3, T4, T5, T6, T7, T8, R>(Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (t1, t2, t3, t4, t5, t6, t7, t8) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(t1, t2, t3, t4, t5, t6, t7, t8);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector)
        => new GenSelectTuple<T1, T2, T3, T4, T5, T6, T7, T8, R>(gen, selector);

    sealed class GenSelect<T1, T2, R>(Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, R> selector)
        => new GenSelect<T1, T2, R>(gen1, gen2, selector);

    sealed class GenSelect<T1, T2, T3, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Func<T1, T2, T3, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Func<T1, T2, T3, R> selector)
        => new GenSelect<T1, T2, T3, R>(gen1, gen2, gen3, selector);

    sealed class GenSelect<T1, T2, T3, T4, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Func<T1, T2, T3, T4, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3, v4);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Func<T1, T2, T3, T4, R> selector)
        => new GenSelect<T1, T2, T3, T4, R>(gen1, gen2, gen3, gen4, selector);

    sealed class GenSelect<T1, T2, T3, T4, T5, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Func<T1, T2, T3, T4, T5, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            if (Size.IsLessThan(min, size)) return default!;
            size.Add(s);
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3, v4, v5);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Func<T1, T2, T3, T4, T5, R> selector)
        => new GenSelect<T1, T2, T3, T4, T5, R>(gen1, gen2, gen3, gen4, gen5, selector);

    sealed class GenSelect<T1, T2, T3, T4, T5, T6, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3, v4, v5, v6);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, R> selector)
        => new GenSelect<T1, T2, T3, T4, T5, T6, R>(gen1, gen2, gen3, gen4, gen5, gen6, selector);

    sealed class GenSelect<T1, T2, T3, T4, T5, T6, T7, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3, v4, v5, v6, v7);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, R> selector)
        => new GenSelect<T1, T2, T3, T4, T5, T6, T7, R>(gen1, gen2, gen3, gen4, gen5, gen6, gen7, selector);

    sealed class GenSelect<T1, T2, T3, T4, T5, T6, T7, T8, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v8 = gen8.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            return selector(v1, v2, v3, v4, v5, v6, v7, v8);
        }
    }
    /// <summary>Projects each element of a generator into a new form.</summary>
    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector)
        => new GenSelect<T1, T2, T3, T4, T5, T6, T7, T8, R>(gen1, gen2, gen3, gen4, gen5, gen6, gen7, gen8, selector);

    sealed class GenSelectTupleCreate<T1, T2>(Gen<T1> gen1, Gen<T2> gen2) : Gen<(T1, T2)>
    {
        public override (T1, T2) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            return (v1, v2);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2)> Select<T1, T2>(this Gen<T1> gen1, Gen<T2> gen2)
        => new GenSelectTupleCreate<T1, T2>(gen1, gen2);

    sealed class GenSelectTupleCreate<T1, T2, T3>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3) : Gen<(T1, T2, T3)>
    {
        public override (T1, T2, T3) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3)> Select<T1, T2, T3>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3)
        => new GenSelectTupleCreate<T1, T2, T3>(gen1, gen2, gen3);

    sealed class GenSelectTupleCreate<T1, T2, T3, T4>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4) : Gen<(T1, T2, T3, T4)>
    {
        public override (T1, T2, T3, T4) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3, v4);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3, T4)> Select<T1, T2, T3, T4>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4)
        => new GenSelectTupleCreate<T1, T2, T3, T4>(gen1, gen2, gen3, gen4);

    sealed class GenSelectTupleCreate<T1, T2, T3, T4, T5>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5) : Gen<(T1, T2, T3, T4, T5)>
    {
        public override (T1, T2, T3, T4, T5) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3, v4, v5);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3, T4, T5)> Select<T1, T2, T3, T4, T5>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5)
        => new GenSelectTupleCreate<T1, T2, T3, T4, T5>(gen1, gen2, gen3, gen4, gen5);

    sealed class GenSelectTupleCreate<T1, T2, T3, T4, T5, T6>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6) : Gen<(T1, T2, T3, T4, T5, T6)>
    {
        public override (T1, T2, T3, T4, T5, T6) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3, v4, v5, v6);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6)> Select<T1, T2, T3, T4, T5, T6>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6)
        => new GenSelectTupleCreate<T1, T2, T3, T4, T5, T6>(gen1, gen2, gen3, gen4, gen5, gen6);

    sealed class GenSelectTupleCreate<T1, T2, T3, T4, T5, T6, T7>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7) : Gen<(T1, T2, T3, T4, T5, T6, T7)>
    {
        public override (T1, T2, T3, T4, T5, T6, T7) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3, v4, v5, v6, v7);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6, T7)> Select<T1, T2, T3, T4, T5, T6, T7>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7)
        => new GenSelectTupleCreate<T1, T2, T3, T4, T5, T6, T7>(gen1, gen2, gen3, gen4, gen5, gen6, gen7);

    sealed class GenSelectTupleCreate<T1, T2, T3, T4, T5, T6, T7, T8>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8) : Gen<(T1, T2, T3, T4, T5, T6, T7, T8)>
    {
        public override (T1, T2, T3, T4, T5, T6, T7, T8) Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v8 = gen8.Generate(pcg, min, out s);
            size.Add(s);
            return (v1, v2, v3, v4, v5, v6, v7, v8);
        }
    }
    /// <summary>Projects each element of a generator into a tuple.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> Select<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8)
        => new GenSelectTupleCreate<T1, T2, T3, T4, T5, T6, T7, T8>(gen1, gen2, gen3, gen4, gen5, gen6, gen7, gen8);

    sealed class GenSelectMany<T, R>(Gen<T> gen, Func<T, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T, R>(this Gen<T> gen, Func<T, IGen<R>> selector)
        => new GenSelectMany<T, R>(gen, selector);

    sealed class GenSelectMany<T1, T2, R>(Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, Gen<R>> selector)
        => new GenSelectMany<T1, T2, R>(gen1, gen2, selector);

    sealed class GenSelectMany<T1, T2, T3, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Func<T1, T2, T3, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Func<T1, T2, T3, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, R>(gen1, gen2, gen3, selector);

    sealed class GenSelectMany<T1, T2, T3, T4, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Func<T1, T2, T3, T4, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Func<T1, T2, T3, T4, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, T4, R>(gen1, gen2, gen3, gen4, selector);

    sealed class GenSelectMany<T1, T2, T3, T4, T5, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Func<T1, T2, T3, T4, T5, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Func<T1, T2, T3, T4, T5, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, T4, T5, R>(gen1, gen2, gen3, gen4, gen5, selector);

    sealed class GenSelectMany<T1, T2, T3, T4, T5, T6, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, T4, T5, T6, R>(gen1, gen2, gen3, gen4, gen5, gen6, selector);

    sealed class GenSelectMany<T1, T2, T3, T4, T5, T6, T7, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6, v7).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, T4, T5, T6, T7, R>(gen1, gen2, gen3, gen4, gen5, gen6, gen7, selector);

    sealed class GenSelectMany<T1, T2, T3, T4, T5, T6, T7, T8, R>(Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v6 = gen6.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v7 = gen7.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v8 = gen8.Generate(pcg, min, out s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6, v7, v8).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, Gen<R>> selector)
        => new GenSelectMany<T1, T2, T3, T4, T5, T6, T7, T8, R>(gen1, gen2, gen3, gen4, gen5, gen6, gen7, gen8, selector);

    sealed class GenSelectManyResult<T1, T2, R>(Gen<T1> gen, Func<T1, IGen<T2>> genSelector, Func<T1, T2, R> resultSelector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = genSelector(v1).Generate(pcg, min?.Below(size), out var s);
            size.Append(s);
            if (Size.IsLessThan(min, size)) return default!;
            return resultSelector(v1, v2);
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen, Func<T1, IGen<T2>> genSelector, Func<T1, T2, R> resultSelector)
        => new GenSelectManyResult<T1, T2, R>(gen, genSelector, resultSelector);

    sealed class GenSelectManyResult<T1, T2, T3, R>(Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, IGen<T3>> genSelector, Func<T1, T2, T3, R> resultSelector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var v1 = gen1.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var v2 = gen2.Generate(pcg, min, out var s);
            size.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
            var v3 = genSelector(v1, v2).Generate(pcg, min?.Below(size), out s);
            size.Append(s);
            if (Size.IsLessThan(min, size)) return default!;
            return resultSelector(v1, v2, v3);
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, IGen<T3>> genSelector, Func<T1, T2, T3, R> resultSelector)
        => new GenSelectManyResult<T1, T2, T3, R>(gen1, gen2, genSelector, resultSelector);

    sealed class GenSelectManyTuple<T1, T2, R>(Gen<(T1, T2)> gen, Func<T1, T2, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, R>(Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, T4, R>(Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3, v4) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, T4, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, T4, T5, R>(Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3, v4, v5) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, T4, T5, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, T4, T5, T6, R>(Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3, v4, v5, v6) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, T4, T5, T6, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, T4, T5, T6, T7, R>(Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3, v4, v5, v6, v7) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6, v7).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, T4, T5, T6, T7, R>(gen, selector);

    sealed class GenSelectManyTuple<T1, T2, T3, T4, T5, T6, T7, T8, R>(Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, IGen<R>> selector) : Gen<R>
    {
        public override R Generate(PCG pcg, Size? min, out Size size)
        {
            var (v1, v2, v3, v4, v5, v6, v7, v8) = gen.Generate(pcg, min, out size);
            if (Size.IsLessThan(min, size)) return default!;
            var vR = selector(v1, v2, v3, v4, v5, v6, v7, v8).Generate(pcg, min?.Below(size), out var sR);
            size.Append(sR);
            return vR;
        }
    }
    /// <summary>Projects each element of a generator to a new generator and flattens into one generator.</summary>
    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, IGen<R>> selector)
        => new GenSelectManyTuple<T1, T2, T3, T4, T5, T6, T7, T8, R>(gen, selector);

    sealed class GenWhere<T>(Gen<T> gen, Func<T, bool> predicate) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var t = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t)) return t;
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<T> Where<T>(this Gen<T> gen, Func<T, bool> predicate)
        => new GenWhere<T>(gen, predicate);

    sealed class GenWhere<T1, T2>(Gen<(T1, T2)> gen, Func<T1, T2, bool> predicate) : Gen<(T1, T2)>
    {
        public override (T1, T2) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2)) return (t1, t2);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2)> Where<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> predicate)
        => new GenWhere<T1, T2>(gen, predicate);

    sealed class GenWhere<T1, T2, T3>(Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, bool> predicate) : Gen<(T1, T2, T3)>
    {
        public override (T1, T2, T3) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3)) return (t1, t2, t3);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3)> Where<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, bool> predicate)
        => new GenWhere<T1, T2, T3>(gen, predicate);

    sealed class GenWhere<T1, T2, T3, T4>(Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, bool> predicate) : Gen<(T1, T2, T3, T4)>
    {
        public override (T1, T2, T3, T4) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3, t4) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3, t4)) return (t1, t2, t3, t4);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3, T4)> Where<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, bool> predicate)
        => new GenWhere<T1, T2, T3, T4>(gen, predicate);

    sealed class GenWhere<T1, T2, T3, T4, T5>(Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, bool> predicate) : Gen<(T1, T2, T3, T4, T5)>
    {
        public override (T1, T2, T3, T4, T5) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3, t4, t5) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3, t4, t5)) return (t1, t2, t3, t4, t5);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3, T4, T5)> Where<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, bool> predicate)
        => new GenWhere<T1, T2, T3, T4, T5>(gen, predicate);

    sealed class GenWhere<T1, T2, T3, T4, T5, T6>(Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, bool> predicate) : Gen<(T1, T2, T3, T4, T5, T6)>
    {
        public override (T1, T2, T3, T4, T5, T6) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3, t4, t5, t6) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3, t4, t5, t6)) return (t1, t2, t3, t4, t5, t6);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6)> Where<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, bool> predicate)
        => new GenWhere<T1, T2, T3, T4, T5, T6>(gen, predicate);

    sealed class GenWhere<T1, T2, T3, T4, T5, T6, T7>(Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, bool> predicate) : Gen<(T1, T2, T3, T4, T5, T6, T7)>
    {
        public override (T1, T2, T3, T4, T5, T6, T7) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3, t4, t5, t6, t7) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3, t4, t5, t6, t7)) return (t1, t2, t3, t4, t5, t6, t7);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6, T7)> Where<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, bool> predicate)
        => new GenWhere<T1, T2, T3, T4, T5, T6, T7>(gen, predicate);

    sealed class GenWhere<T1, T2, T3, T4, T5, T6, T7, T8>(Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> predicate) : Gen<(T1, T2, T3, T4, T5, T6, T7, T8)>
    {
        public override (T1, T2, T3, T4, T5, T6, T7, T8) Generate(PCG pcg, Size? min, out Size size)
        {
            int i = Check.WhereLimit;
            while (i-- > 0)
            {
                var (t1, t2, t3, t4, t5, t6, t7, t8) = gen.Generate(pcg, min, out size);
                if (Size.IsLessThan(min, size)) return default!;
                if (predicate(t1, t2, t3, t4, t5, t6, t7, t8)) return (t1, t2, t3, t4, t5, t6, t7, t8);
            }
            ThrowHelper.ThrowFailingWhereMaxCount();
            size = default!;
            return default!;
        }
    }
    /// <summary>Filters the elements of a generator based on a predicate.</summary>
    public static Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> Where<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> predicate)
        => new GenWhere<T1, T2, T3, T4, T5, T6, T7, T8>(gen, predicate);

    sealed class GenOneOfConst<T>(params T[] constants) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next((uint)constants.Length);
            size = new Size(i);
            return constants[i];
        }
    }
    sealed class GenOneOfConstPow2<T>(params T[] constants) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next() & (uint)(constants.Length - 1);
            size = new Size(i);
            return constants[i];
        }
    }
    /// <summary>Create a generator where the element is one of the constants.</summary>
    public static Gen<T> OneOfConst<T>(params T[] constants)
    {
        if (constants is null) ThrowHelper.Throw("Gen.OneOfConst constants is null");
        if (constants.Length == 0) ThrowHelper.Throw("Gen.OneOfConst constants is empty");
        return HashHelper.IsPow2(constants.Length) ? new GenOneOfConstPow2<T>(constants) : new GenOneOfConst<T>(constants);
    }

    sealed class GenOneOf<T>(params IGen<T>[] gens) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next((uint)gens.Length);
            var next = i == min?.I ? min.Next : null;
            var r = gens[i].Generate(pcg, next, out size);
            size = new Size(i, size);
            return r;
        }
    }
    sealed class GenOneOfPow2<T>(params IGen<T>[] gens) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next() & (uint)(gens.Length - 1);
            var next = i == min?.I ? min.Next : null;
            var r = gens[i].Generate(pcg, next, out size);
            size = new Size(i, size);
            return r;
        }
    }
    /// <summary>Create a generator where the element is generated from one of the generators.</summary>
    public static Gen<T> OneOf<T>(params IGen<T>[] gens)
    {
        if (gens is null) ThrowHelper.Throw("Gen.OneOf gens is null");
        if (gens.Length == 0) ThrowHelper.Throw("Gen.OneOf gens is empty");
        return HashHelper.IsPow2(gens.Length) ? new GenOneOfPow2<T>(gens) : new GenOneOf<T>(gens);
    }

    /// <summary>Create a generator for an enum.</summary>
    public static Gen<T> Enum<T>() where T : Enum
        => OneOfConst((T[])System.Enum.GetValues(typeof(T)));

    sealed class GenFrequencyConst<T>(uint total, params (int Frequency, T Constant)[] constants) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var v = (int)pcg.Next(total);
            size = new Size((ulong)v);
            for (var j = 0; j < constants.Length; j++)
            {
                v -= constants[j].Frequency;
                if (v < 0)
                    return constants[j].Constant;
            }
            return default!;
        }
    }
    sealed class GenFrequencyConstPow2<T>(uint total, params (int Frequency, T Constant)[] constants) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var v = (int)(pcg.Next() & (total - 1));
            size = new Size((ulong)v);
            for (var j = 0; j < constants.Length; j++)
            {
                v -= constants[j].Frequency;
                if (v < 0)
                    return constants[j].Constant;
            }
            return default!;
        }
    }
    /// <summary>Create a generator where the element is one of the constants weighted by the frequency.</summary>
    public static Gen<T> FrequencyConst<T>(params (int Frequency, T Constant)[] constants)
    {
        if (constants is null) ThrowHelper.Throw("Gen.FrequencyConst constants is null");
        if (constants.Length == 0) ThrowHelper.Throw("Gen.FrequencyConst constants is empty");
        uint total = 0;
        foreach (var (i, _) in constants) total += (uint)i;
        return HashHelper.IsPow2(total) ? new GenFrequencyConstPow2<T>(total, constants) : new GenFrequencyConst<T>(total, constants);
    }

    sealed class GenFrequency<T>(uint total, params (int Frequency, IGen<T> Generator)[] gens) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var nSize = pcg.Next(total);
            var v = (int)nSize;
            for (var i = 0; i < gens.Length; i++)
            {
                v -= gens[i].Frequency;
                if (v < 0)
                {
                    var next = nSize == min?.I ? min.Next : null;
                    var r = gens[i].Generator.Generate(pcg, next, out size);
                    size = new Size(nSize, size);
                    return r;
                }
            }
            size = new Size(0);
            return default!;
        }
    }
    sealed class GenFrequencyPow2<T>(uint total, params (int Frequency, IGen<T> Generator)[] gens) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            var nSize = pcg.Next() & (total - 1);
            var v = (int)nSize;
            for (var i = 0; i < gens.Length; i++)
            {
                v -= gens[i].Frequency;
                if (v < 0)
                {
                    var next = nSize == min?.I ? min.Next : null;
                    var r = gens[i].Generator.Generate(pcg, next, out size);
                    size = new Size(nSize, size);
                    return r;
                }
            }
            size = new Size(0);
            return default!;
        }
    }
    /// <summary>Create a generator where the element is generated by one of the generators weighted by the frequency.</summary>
    public static Gen<T> Frequency<T>(params (int Frequency, IGen<T> Generator)[] gens)
    {
        if (gens is null) ThrowHelper.Throw("Gen.Frequency gens is null");
        if (gens.Length == 0) ThrowHelper.Throw("Gen.Frequency gens is empty");
        uint total = 0;
        foreach (var (i, _) in gens) total += (uint)i;
        return HashHelper.IsPow2(total) ? new GenFrequencyPow2<T>(total, gens) : new GenFrequency<T>(total, gens);
    }

    sealed class GenRecursive<T>(Func<Gen<T>> gen) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            return gen().Generate(pcg, null, out size);
        }
    }
    /// <summary>
    /// Recursively generate a type.
    /// </summary>
    /// <param name="f">The function to create the generator give an instance of the generator.</param>
    public static Gen<T> Recursive<T>(Func<Gen<T>, Gen<T>> f)
    {
        Gen<T>? gen = null;
        gen = f(new GenRecursive<T>(() => gen!));
        return gen;
    }

    /// <summary>
    /// Recursively generate a type with the depth of generation.
    /// </summary>
    /// <param name="f">The function to create the generator give the depth and an instance of the generator.</param>
    public static Gen<T> Recursive<T>(Func<int, Gen<T>, Gen<T>> f)
    {
        Gen<T> gen(int i) => f(i, new GenRecursive<T>(() => gen(i + 1)));
        return gen(0);
    }

    sealed class GenRecursiveMap<T>(Func<Gen<T>> gen, GenMap<T> map) : Gen<T>
    {
        public override T Generate(PCG pcg, Size? min, out Size size)
        {
            return map(gen().Generate(pcg, null, out size), ref size);
        }
    }
    /// <summary>
    /// Recursively generate a type with a map of type and size.
    /// </summary>
    /// <param name="f">The function to create the generator give an instance of the generator.</param>
    /// <param name="map">The type and size map function.</param>
    public static Gen<T> Recursive<T>(Func<Gen<T>, Gen<T>> f, GenMap<T> map)
    {
        Gen<T>? gen = null;
        gen = f(new GenRecursiveMap<T>(() => gen!, map));
        return gen;
    }

    /// <summary>
    /// Recursively generate a type with the depth of generation and a map of type and size.
    /// </summary>
    /// <param name="f">The function to create the generator give the depth and an instance of the generator.</param>
    /// <param name="map">The type and size map function.</param>
    public static Gen<T> Recursive<T>(Func<int, Gen<T>, Gen<T>> f, GenMap<T> map)
    {
        Gen<T> gen(int i) => f(i, new GenRecursiveMap<T>(() => gen(i + 1), map));
        return gen(0);
    }

    sealed class GenClone<T>(Gen<T> gen) : Gen<(T, T)>
    {
        public override (T, T) Generate(PCG pcg, Size? min, out Size size)
        {
            var seed = pcg.Seed;
            var stream = pcg.Stream;
            var r1 = gen.Generate(pcg, min, out size);
            var r2 = gen.Generate(new PCG(stream, seed), min, out _);
            return (r1, r2);
        }
    }
    /// <summary>Create a second exact clone by running the generator again with the same seed.</summary>
    public static Gen<(T, T)> Clone<T>(this Gen<T> gen)
        => new GenClone<T>(gen);

    public static GenDictionary<K, V> Dictionary<K, V>(this Gen<K> genK, Gen<V> genV) where K : notnull => new(genK, genV);

    public static GenSortedDictionary<K, V> SortedDictionary<K, V>(this Gen<K> genK, Gen<V> genV) where K : notnull => new(genK, genV);

    static void ShuffleInPlace<T>(IList<T> a, PCG pcg, int lower)
    {
        for (int i = a.Count - 1; i > lower; i--)
        {
            int j = (int)pcg.Next((uint)(i + 1));
            if (i != j)
            {
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
    }

    sealed class GenShuffleArray<T>(T[] constants) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            var array = (T[])constants.Clone();
            ShuffleInPlace(array, pcg, 0);
            size = new Size(0);
            return array;
        }
    }
    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<T[]> Shuffle<T>(T[] constants)
        => new GenShuffleArray<T>(constants);

    sealed class GenShuffleArrayLength<T>(T[] constants, int length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            var a = (T[])constants.Clone();
            size = new Size(0);
            int lower = Math.Max(a.Length - length, 0);
            ShuffleInPlace(a, pcg, lower);
            if (lower == 0) return a;
            var r = new T[length];
            for (int i = 0; i < r.Length; i++)
                r[i] = a[i + lower];
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<T[]> Shuffle<T>(T[] constants, int length)
        => new GenShuffleArrayLength<T>(constants, length);

    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<T[]> Shuffle<T>(T[] a, int start, int finish) =>
        Int[start, finish].SelectMany(i => Shuffle(a, i));

    /// <summary>Shuffle the generated elements.</summary>
    public static Gen<T[]> Shuffle<T>(this Gen<T[]> gen, int length) =>
        SelectMany(gen, a => Shuffle(a, length));

    /// <summary>Shuffle the generated elements.</summary>
    public static Gen<T[]> Shuffle<T>(this Gen<T[]> gen, int start, int finish) =>
        SelectMany(gen, Int[start, finish], (a, l) => Shuffle(a, l));

    sealed class GenShuffleList<T>(List<T> constants) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
        {
            var list = new List<T>(constants);
            ShuffleInPlace(list, pcg, 0);
            size = new Size(0);
            return list;
        }
    }
    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<List<T>> Shuffle<T>(List<T> list)
        => new GenShuffleList<T>(list);

    sealed class GenShuffleListLength<T>(List<T> constants, int length) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
        {
            var list = new List<T>(constants);
            size = new Size(0);
            int lower = Math.Max(list.Count - length, 0);
            ShuffleInPlace(list, pcg, lower);
            if (lower == 0) return list;
            var r = new List<T>(length);
            for (int i = 0; i < length; i++)
                r.Add(list[i + lower]);
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<List<T>> Shuffle<T>(List<T> list, int length)
        => new GenShuffleListLength<T>(list, length);

    /// <summary>Create a generator by shuffling the elements.</summary>
    public static Gen<List<T>> Shuffle<T>(List<T> a, int start, int finish) =>
        SelectMany(Int[start, finish], l => Shuffle(a, l));

    /// <summary>Create a generator by shuffling the generated elements.</summary>
    public static Gen<List<T>> Shuffle<T>(this Gen<List<T>> gen, int length) =>
        SelectMany(gen, a => Shuffle(a, length));

    /// <summary>Create a generator by shuffling the generated elements.</summary>
    public static Gen<List<T>> Shuffle<T>(this Gen<List<T>> gen, int start, int finish) =>
        SelectMany(gen, Int[start, finish], (a, l) => Shuffle(a, l));

    sealed class GenShuffleSelectArray<T>(Gen<T>[] gens) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            size = new Size(0);
            var r = new T[gens.Length];
            for (int i = 0; i < gens.Length; i++)
            {
                r[i] = gens[i].Generate(pcg, min, out var s);
                size.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
            }
            ShuffleInPlace(r, pcg, 0);
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<T[]> ShuffleSelect<T>(this Gen<T>[] gens) => new GenShuffleSelectArray<T>(gens);

    sealed class GenShuffleSelectArrayLength<T>(Gen<T>[] gens, int length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            var array = (Gen<T>[])gens.Clone();
            int lower = Math.Max(gens.Length - length, 0);
            ShuffleInPlace(array, pcg, lower);
            size = new Size(0);
            var r = new T[length];
            for (int i = 0; i < length; i++)
            {
                r[i] = array[i + lower].Generate(pcg, min, out var s);
                size.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
            }
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<T[]> ShuffleSelect<T>(this Gen<T>[] gens, int length) =>
        new GenShuffleSelectArrayLength<T>(gens, length);

    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<T[]> ShuffleSelect<T>(this Gen<T>[] gens, int start, int finish) =>
        SelectMany(Int[start, finish], l => ShuffleSelect(gens, l));

    sealed class GenShuffleSelectList<T>(List<Gen<T>> gens) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
        {
            size = new Size(0);
            var r = new List<T>(gens.Count);
            foreach (var gen in gens)
            {
                r.Add(gen.Generate(pcg, min, out var s));
                size.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
            }
            ShuffleInPlace(r, pcg, 0);
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<List<T>> ShuffleSelect<T>(this List<Gen<T>> gens) => new GenShuffleSelectList<T>(gens);

    sealed class GenShuffleSelectListLength<T>(List<Gen<T>> gens, int length) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
        {
            var array = gens.ToArray();
            int lower = Math.Max(array.Length - length, 0);
            ShuffleInPlace(array, pcg, lower);
            size = new Size(0);
            var r = new List<T>(length);
            for (int i = 0; i < length; i++)
            {
                r.Add(array[i + lower].Generate(pcg, min, out var s));
                size.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
            }
            return r;
        }
    }
    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<List<T>> ShuffleSelect<T>(this List<Gen<T>> gens, int length) =>
        new GenShuffleSelectListLength<T>(gens, length);

    /// <summary>Create a generator by shuffling the generator elements and generating each.</summary>
    public static Gen<List<T>> ShuffleSelect<T>(this List<Gen<T>> gens, int start, int finish) =>
        SelectMany(Int[start, finish], l => ShuffleSelect(gens, l));

    sealed class GenNullable<T>(Gen<T> gen, uint nullLimit) : Gen<T?> where T : struct
    {
        public override T? Generate(PCG pcg, Size? min, out Size size)
        {
            if (pcg.Next() < nullLimit)
            {
                size = new Size(0);
                return default;
            }
            var next = min?.I == 1 ? min.Next : null;
            var r = gen.Generate(pcg, next, out size);
            size = new Size(1, size);
            return new T?(r);
        }
    }

    /// <summary>Create a generator making the struct element nullable.</summary>
    public static Gen<T?> Nullable<T>(this Gen<T> gen, double nullFraction = 0.2) where T : struct
        => new GenNullable<T>(gen, (uint)(nullFraction * uint.MaxValue));

    sealed class GenNull<T>(Gen<T> gen, uint nullLimit) : Gen<T?> where T : class
    {
        public override T? Generate(PCG pcg, Size? min, out Size size)
        {
            if (pcg.Next() < nullLimit)
            {
                size = new Size(0);
                return null;
            }
            var next = min?.I == 1 ? min.Next : null;
            var r = gen.Generate(pcg, next, out size);
            size = new Size(1, size);
            return r;
        }
    }

    /// <summary>Create a generator making the class element nullable.</summary>
    public static Gen<T?> Null<T>(this Gen<T> gen, double nullFraction = 0.2) where T : class
    {
        return new GenNull<T>(gen, (uint)(nullFraction * uint.MaxValue));
    }

    public static GenOperation<T> Operation<T>(string name, Action<T> action) => GenOperation.Create(name, action);
    public static GenOperation<T> Operation<T>(string name, Func<T, Task> async) => GenOperation.Create(name, (T t) => async(t).GetAwaiter().GetResult());
    public static GenOperation<T> Operation<T>(Action<T> action) => GenOperation.Create(action);
    public static GenOperation<T> Operation<T>(Func<T, Task> async) => GenOperation.Create((T t) => async(t).GetAwaiter().GetResult());
    public static GenOperation<Actual, Model> Operation<Actual, Model>(string name, Action<Actual> actual, Action<Model> model) => GenOperation.Create(name, actual, model);
    public static GenOperation<Actual, Model> Operation<Actual, Model>(Action<Actual> actual, Action<Model> model) => GenOperation.Create(actual, model);

    /// <summary>Generator for bool.</summary>
    public static readonly GenBool Bool = new();
    /// <summary>Generator for sbyte.</summary>
    public static readonly GenSByte SByte = new();
    /// <summary>Generator for byte.</summary>
    public static readonly GenByte Byte = new();
    /// <summary>Generator for short.</summary>
    public static readonly GenShort Short = new();
    /// <summary>Generator for ushort.</summary>
    public static readonly GenUShort UShort = new();
    /// <summary>Generator for int.</summary>
    public static readonly GenInt Int = new();
    internal static readonly Gen<int> Int9999 = Int[1, 9999];
    public static readonly GenUInt UInt = new();
    /// <summary>Generator for uint in the range 0 to 3 inclusive.</summary>
    public static readonly GenUInt4 UInt4 = new();
    /// <summary>Generator for uint in the range 0 to 7 inclusive.</summary>
    public static readonly GenUInt8 UInt8 = new();
    /// <summary>Generator for uint in the range 0 to 15 inclusive.</summary>
    public static readonly GenUInt16 UInt16 = new();
    /// <summary>Generator for uint in the range 0 to 31 inclusive.</summary>
    public static readonly GenUInt32 UInt32 = new();
    /// <summary>Generator for uint in the range 0 to 63 inclusive.</summary>
    public static readonly GenUInt64 UInt64 = new();
    /// <summary>Generator for uint in the range 0 to 127 inclusive.</summary>
    public static readonly GenUInt128 UInt128 = new();
    /// <summary>Generator for uint in the range 0 to 255 inclusive.</summary>
    public static readonly GenUInt256 UInt256 = new();
    /// <summary>Generator for uint in the range 0 to 511 inclusive.</summary>
    public static readonly GenUInt512 UInt512 = new();
    /// <summary>Generator for uint in the range 0 to 1023 inclusive.</summary>
    public static readonly GenUInt1024 UInt1024 = new();
    /// <summary>Generator for uint in the range 0 to 2047 inclusive.</summary>
    public static readonly GenUInt2048 UInt2048 = new();
    /// <summary>Generator for long.</summary>
    public static readonly GenLong Long = new();
    /// <summary>Generator for ulong.</summary>
    public static readonly GenULong ULong = new();
    /// <summary>Generator for float.</summary>
    public static readonly GenFloat Float = new();
    /// <summary>Generator for float.</summary>
    public static readonly GenFloat Single = Float;
    /// <summary>Generator for double.</summary>
    public static readonly GenDouble Double = new();
    /// <summary>Generator for decimal.</summary>
    public static readonly GenDecimal Decimal = new();
    /// <summary>Generator for Date.</summary>
    public static readonly GenDate Date = new();
    /// <summary>Generator for DateOnly.</summary>
    public static readonly GenDateOnly DateOnly = new();
    /// <summary>Generator for DateTime.</summary>
    public static readonly GenDateTime DateTime = new();
    /// <summary>Generator for TimeOnly.</summary>
    public static readonly GenTimeOnly TimeOnly = new();
    /// <summary>Generator for TimeSpan.</summary>
    public static readonly GenTimeSpan TimeSpan = new();
    /// <summary>Generator for DateTimeOffset.</summary>
    public static readonly GenDateTimeOffset DateTimeOffset = new();
    /// <summary>Generator for Guid.</summary>
    public static readonly GenGuid Guid = new();
    /// <summary>Generator for char.</summary>
    public static readonly GenChar Char = new();
    /// <summary>Generator for string.</summary>
    public static readonly GenString String = new();
    /// <summary>Generator for a PCG seed.</summary>
    public static readonly GenSeed Seed = new();
}

public sealed class GenBool : Gen<bool>
{
    public override bool Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 1U;
        size = new Size(i);
        return i == 1U;
    }
}

public sealed class GenSByte : Gen<sbyte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong Zigzag(sbyte i) => (ulong)((i << 1) ^ (i >> 7));
    public override sbyte Generate(PCG pcg, Size? min, out Size size)
    {
        var i = (sbyte)pcg.Next();
        size = new Size(Zigzag(i));
        return i;
    }
    sealed class Range(sbyte start, uint length) : Gen<sbyte>
    {
        public override sbyte Generate(PCG pcg, Size? min, out Size size)
        {
            var i = (sbyte)(start + pcg.Next(length));
            size = new Size(Zigzag(i));
            return i;
        }
    }

    /// <summary>Generate sbyte uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<sbyte> this[sbyte start, sbyte finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (uint)(finish - start) + 1U);
        }
    }
}

public sealed class GenByte : Gen<byte>
{
    public override byte Generate(PCG pcg, Size? min, out Size size)
    {
        byte i = (byte)pcg.Next();
        size = new Size(i);
        return i;
    }
    sealed class Range(byte start, uint length) : Gen<byte>
    {
        public override byte Generate(PCG pcg, Size? min, out Size size)
        {
            var i = (byte)(start + pcg.Next(length));
            size = new Size(i);
            return i;
        }
    }

    /// <summary>Generate byte uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<byte> this[byte start, byte finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (uint)finish - start + 1U);
        }
    }
}

public sealed class GenShort : Gen<short>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Zigzag(short i) => (ushort)(i << 1 ^ i >> 31);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short Unzigzag(ushort i) => (short)((i >> 1) ^ -(int)(i & 1U));
    public override short Generate(PCG pcg, Size? min, out Size size)
    {
        uint s = pcg.Next() & 15U;
        ushort i = (ushort)(1U << (int)s);
        i = (ushort)((pcg.Next() & (i - 1) | i) - 1);
        size = new Size(s << 11 | i & 0x7FFUL);
        return (short)-Unzigzag(i);
    }
    sealed class Range(short start, uint length) : Gen<short>
    {
        public override short Generate(PCG pcg, Size? min, out Size size)
        {
            var i = (short)(start + pcg.Next(length));
            size = new Size(Zigzag(i));
            return i;
        }
    }

    /// <summary>Generate short uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<short> this[short start, short finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (uint)(finish - start + 1));
        }
    }
}

public sealed class GenUShort : Gen<ushort>
{
    public override ushort Generate(PCG pcg, Size? min, out Size size)
    {
        var i = (ushort)pcg.Next();
        size = new Size(i);
        return i;
    }
    sealed class Range(ushort start, ushort length) : Gen<ushort>
    {
        public override ushort Generate(PCG pcg, Size? min, out Size size)
        {
            var i = (ushort)(start + pcg.Next(length));
            size = new Size(i);
            return i;
        }
    }

    /// <summary>Generate ushort uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<ushort> this[ushort start, ushort finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (ushort)(finish - start + 1));
        }
    }
}

public sealed class GenInt : Gen<int>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Zigzag(int i) => (uint)(i << 1 ^ i >> 31);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Unzigzag(uint i) => (int)(i >> 1) ^ -(int)(i & 1U);
    public override int Generate(PCG pcg, Size? min, out Size size)
    {
        uint s = pcg.Next() & 31U;
        uint i = 1U << (int)s;
        i = (pcg.Next() & (i - 1) | i) - 1;
        size = new Size(s << 27 | i & 0x7FFFFFFUL);
        return -Unzigzag(i);
    }
    sealed class GenUniform : Gen<int>
    {
        public override int Generate(PCG pcg, Size? min, out Size size)
        {
            int i = (int)pcg.Next();
            size = new Size(Zigzag(i));
            return i;
        }
    }
    /// <summary>Generate an int uniformly distributed with all values.</summary>
    public Gen<int> Uniform = new GenUniform();
    sealed class GenPositive : Gen<int>
    {
        public override int Generate(PCG pcg, Size? min, out Size size)
        {
            uint s = pcg.Next(31);
            int i = 1 << (int)s;
            i = (int)pcg.Next() & (i - 1) | i;
            size = new Size(s << 27 | (ulong)i & 0x7FFFFFFUL);
            return i;
        }
    }
    /// <summary>Generate a positive int in the range 1 to int.MaxValue inclusive.</summary>
    public Gen<int> Positive = new GenPositive();
    sealed class GenNonNegative : Gen<int>
    {
        public override int Generate(PCG pcg, Size? min, out Size size)
        {
            uint s = pcg.Next(31);
            int i = 1 << (int)s;
            i = ((int)pcg.Next() & (i - 1) | i) - 1;
            size = new Size((s << 27 | (ulong)i & 0x7FFFFFFUL) + 1UL);
            return i;
        }
    }
    /// <summary>Generate a non-negative int in the range 0 to int.MaxValue inclusive.</summary>
    public Gen<int> NonNegative = new GenNonNegative();
    sealed class Range(int start, uint length) : Gen<int>
    {
        public override int Generate(PCG pcg, Size? min, out Size size)
        {
            int i = (int)(start + pcg.Next(length));
            size = new Size(Zigzag(i));
            return i;
        }
    }
    /// <summary>Generate int uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<int> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (uint)(finish - start + 1));
        }
    }
}

public sealed class GenUInt : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        uint s = pcg.Next() & 31U;
        uint i = 1U << (int)s;
        i = (pcg.Next() & (i - 1) | i) - 1;
        size = new Size(s << 27 | i & 0x7FFF_FFFUL);
        return i;
    }
    sealed class Range(uint start, uint length) : Gen<uint>
    {
        public override uint Generate(PCG pcg, Size? min, out Size size)
        {
            uint i = start + pcg.Next(length);
            size = new Size(i);
            return i;
        }
    }
    /// <summary>Generate uint uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<uint> this[uint start, uint finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, finish - start + 1U);
        }
    }
    sealed class GenUniform : Gen<uint>
    {
        public override uint Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next();
            size = new Size(i);
            return i;
        }
    }
    /// <summary>Generate a uint uniformly distributed with all values.</summary>
    public Gen<uint> Uniform = new GenUniform();
}
public sealed class GenUInt4 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 3;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt8 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 7;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt16 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 15;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt32 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 31;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt64 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 63;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt128 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 127;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt256 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 255;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt512 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 511;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt1024 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 1023;
        size = new Size(i);
        return i;
    }
}
public sealed class GenUInt2048 : Gen<uint>
{
    public override uint Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next() & 2047;
        size = new Size(i);
        return i;
    }
}
public sealed class GenLong : Gen<long>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Zigzag(long i) => (ulong)(i << 1 ^ i >> 63);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Unzigzag(ulong i) => (long)(i >> 1) ^ -(long)(i & 1UL);
    public override long Generate(PCG pcg, Size? min, out Size size)
    {
        uint s = pcg.Next() & 63U;
        ulong i = 1UL << (int)s;
        i = (pcg.Next64() & (i - 1UL) | i) - 1UL;
        size = new Size((ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU);
        return -Unzigzag(i);
    }
    sealed class Range(long start, ulong length) : Gen<long>
    {
        public override long Generate(PCG pcg, Size? min, out Size size)
        {
            var i = start + (long)pcg.Next64(length);
            size = new Size(Zigzag(i));
            return i;
        }
    }
    /// <summary>Generate long uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<long> this[long start, long finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, (ulong)(finish - start + 1));
        }
    }
    sealed class GenUniform : Gen<long>
    {
        public override long Generate(PCG pcg, Size? min, out Size size)
        {
            var i = (long)pcg.Next64();
            size = new Size(Zigzag(i));
            return i;
        }
    }
    /// <summary>Generate a long uniformly distributed with all values.</summary>
    public Gen<long> Uniform = new GenUniform();
}

public sealed class GenULong : Gen<ulong>
{
    public override ulong Generate(PCG pcg, Size? min, out Size size)
    {
        uint s = pcg.Next() & 63U;
        ulong i = 1UL << (int)s;
        i = (pcg.Next64() & (i - 1UL) | i) - 1UL;
        size = new Size((ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU);
        return i;
    }
    sealed class Range(ulong start, ulong length) : Gen<ulong>
    {
        public override ulong Generate(PCG pcg, Size? min, out Size size)
        {
            var i = start + pcg.Next64(length);
            size = new Size(i);
            return i;
        }
    }
    public Gen<ulong> this[ulong start, ulong finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range(start, finish - start + 1UL);
        }
    }
    sealed class GenUniform : Gen<ulong>
    {
        public override ulong Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64();
            size = new Size(i);
            return i;
        }
    }
    /// <summary>Generate a ulong uniformly distributed with all values.</summary>
    public Gen<ulong> Uniform = new GenUniform();
}

public sealed class GenFloat : Gen<float>
{
    [StructLayout(LayoutKind.Explicit)]
    struct FloatConverter
    {
        [FieldOffset(0)] public uint I;
        [FieldOffset(0)] public float F;
    }

    readonly static Gen<float> DefaultFloat = Gen.Float[-1e25f, 1e25f];
    public override float Generate(PCG pcg, Size? min, out Size size)
        => DefaultFloat.Generate(pcg, min, out size);

    sealed class GenEvenlyDistributed(float start, float length) : Gen<float>
    {
        public override float Generate(PCG pcg, Size? min, out Size size)
        {
            uint i = pcg.Next() >> 9;
            size = new Size(i);
            return new FloatConverter { I = i | 0x3F800000 }.F * length + start;
        }
    }
    private static Gen<float> EvenlyDistributed(float start, float finish)
    {
        finish -= start;
        return new GenEvenlyDistributed(start - finish, finish);
    }
    /// <summary>Generate float in the range <paramref name="start"/> to <paramref name="finish"/>.</summary>
    public Gen<float> this[float start, float finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            const int denominator = 99;
            const int minExp = -99;
            static Gen<int> GenInt(float start, float finish)
                => start <= int.MinValue && finish >= int.MaxValue ? Gen.Int
                 : Gen.Int[(int)Math.Max(Math.Ceiling(start), int.MinValue + 1), (int)Math.Min(Math.Floor(finish), int.MaxValue - 1)];
            var myGens = new (int, IGen<float>)[4];
            if (Math.Ceiling(start) <= Math.Floor(start))
                myGens[0] = (1, GenInt(start, finish).Select(i => (float)i));
            if (Math.Ceiling(start * denominator) <= Math.Floor(finish * denominator))
            {
                var lower = denominator - 1;
                while (Math.Ceiling(start * lower) <= Math.Floor(finish * lower) && lower > 1)
                    lower--;
                var rational = Gen.Int[lower + 1, denominator]
                    .SelectMany(den => GenInt(start * den, finish * den)
                    .Select(num => (float)num / den))
                    .Where(r => r >= start && r <= finish);
                myGens[1] = (1, rational);
            }
            Gen<float>? exponential = null;
            if (start <= 0 && finish >= 0)
            {
                var startExp = (int)Math.Ceiling(Math.Log10(Math.Abs(start))) - 3;
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(finish))) - 3;
                if (startExp >= minExp && finishExp >= minExp)
                    exponential = Gen.OneOf(
                        Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => (float)Math.Pow(10, e) * m),
                        Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -(float)Math.Pow(10, e) * m));
                else if (startExp >= minExp)
                    exponential = Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -(float)Math.Pow(10, e) * m);
                else if (finishExp >= minExp)
                    exponential = Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => (float)Math.Pow(10, e) * m);
            }
            else if (start >= 0 && finish >= 0)
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs(start)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(finish))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => (float)Math.Pow(10, e) * m);
            }
            else
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs(finish)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(start))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => -(float)Math.Pow(10, e) * m);
            }
            if (exponential is not null)
                myGens[2] = (1, exponential.Where(r => r >= start && r <= finish));
            myGens[3] = (1, EvenlyDistributed(start, finish));
            return Gen.Frequency(myGens);
        }
    }
    sealed class GenUnit : Gen<float>
    {
        public override float Generate(PCG pcg, Size? min, out Size size)
        {
            uint i = pcg.Next() >> 9;
            size = new Size(i);
            return new FloatConverter { I = i | 0x3F800000 }.F - 1f;
        }
    }
    /// <summary>In the range 0.0f &lt;= x &lt; 1.0f.</summary>
    public Gen<float> Unit = new GenUnit();
    sealed class GenOneTwo : Gen<float>
    {
        public override float Generate(PCG pcg, Size? min, out Size size)
        {
            uint i = pcg.Next() >> 9;
            size = new Size(i);
            return new FloatConverter { I = i | 0x3F800000 }.F;
        }
    }
    /// <summary>In the range 1.0f &lt;= x &lt; 2.0f.</summary>
    public Gen<float> OneTwo = new GenOneTwo();
    static float MakeSpecial(uint i) => (i & 0xFU) switch
    {
        0x0U => float.NaN,
        0x1U => float.PositiveInfinity,
        0x2U => float.NegativeInfinity,
        0x3U => float.MaxValue,
        0x4U => float.MinValue,
        0x5U => float.Epsilon,
        0x6U => -float.Epsilon,
        0x7U => 1f,
        0x8U => -1f,
        0x9U => 2f,
        0xAU => -2f,
        0xBU => 3f,
        0xCU => -3f,
        0xDU => 4f,
        0xEU => -4f,
        _ => 0f,
    };
    sealed class GenSpecial : Gen<float>
    {
        public override float Generate(PCG pcg, Size? min, out Size size)
        {
            uint i = pcg.Next();
            size = new Size(i);
            return (i & 0xF0U) == 0xD0U ? MakeSpecial(i) : new FloatConverter { I = i }.F;
        }
    }
    /// <summary>With more special values like nan, inf, max, epsilon, -2, -1, 0, 1, 2.</summary>
    public readonly Gen<float> Special = new GenSpecial();
}

public sealed class GenDouble : Gen<double>
{
    readonly static Gen<double> DefaultDouble = Gen.Double[-1e50, 1e50];
    public override double Generate(PCG pcg, Size? min, out Size size)
        => DefaultDouble.Generate(pcg, min, out size);
    sealed class GenEvenlyDistributed(double start, double length) : Gen<double>
    {
        public override double Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64() >> 12;
            size = new Size(i);
            return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * length + start;
        }
    }
    private static Gen<double> EvenlyDistributed(double start, double finish)
    {
        finish -= start;
        return new GenEvenlyDistributed(start - finish, finish);
    }

    /// <summary>Generate double in the range <paramref name="start"/> to <paramref name="finish"/>.</summary>
    public Gen<double> this[double start, double finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            const int denominator = 99;
            const int minExp = -99;
            static Gen<int> GenInt(double start, double finish)
                => start <= int.MinValue && finish >= int.MaxValue ? Gen.Int
                 : Gen.Int[(int)Math.Max(Math.Ceiling(start), int.MinValue + 1), (int)Math.Min(Math.Floor(finish), int.MaxValue - 1)];
            var myGens = new (int, IGen<double>)[4];
            if (Math.Ceiling(start) <= Math.Floor(start))
                myGens[0] = (1, GenInt(start, finish).Select(i => (double)i));
            if (Math.Ceiling(start * denominator) <= Math.Floor(finish * denominator))
            {
                var lower = denominator - 1;
                while (Math.Ceiling(start * lower) <= Math.Floor(finish * lower) && lower > 1)
                    lower--;
                var rational = Gen.Int[lower + 1, denominator]
                    .SelectMany(den => GenInt(start * den, finish * den)
                    .Select(num => (double)num / den));
                myGens[1] = (1, rational);
            }
            Gen<double>? exponential = null;
            if (start <= 0 && finish >= 0)
            {
                var startExp = (int)Math.Ceiling(Math.Log10(Math.Abs(start))) - 3;
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(finish))) - 3;
                if (startExp >= minExp && finishExp >= minExp)
                    exponential = Gen.OneOf(
                        Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => Math.Pow(10, e) * m),
                        Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -Math.Pow(10, e) * m));
                else if (startExp >= minExp)
                    exponential = Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -Math.Pow(10, e) * m);
                else if (finishExp >= minExp)
                    exponential = Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => Math.Pow(10, e) * m);
            }
            else if (start >= 0 && finish >= 0)
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs(start)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(finish))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => Math.Pow(10, e) * m);
            }
            else
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs(finish)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs(start))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => -Math.Pow(10, e) * m);
            }
            if (exponential is not null)
                myGens[2] = (1, exponential.Where(r => r >= start && r <= finish));
            myGens[3] = (1, EvenlyDistributed(start, finish));
            return Gen.Frequency(myGens);
        }
    }
    sealed class GenUnit : Gen<double>
    {
        public override double Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64() >> 12;
            size = new Size(i);
            return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1.0;
        }
    }
    /// <summary>In the range 0.0 &lt;= x &lt; 1.0.</summary>
    public Gen<double> Unit = new GenUnit();
    sealed class GenOneTwo : Gen<double>
    {
        public override double Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64() >> 12;
            size = new Size(i);
            return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000);
        }
    }
    /// <summary>In the range 1.0 &lt;= x &lt; 2.0.</summary>
    public Gen<double> OneTwo = new GenOneTwo();
    static double MakeSpecial(ulong i) => (i & 0xFUL) switch
    {
        0x0UL => double.NaN,
        0x1UL => double.PositiveInfinity,
        0x2UL => double.NegativeInfinity,
        0x3UL => double.MaxValue,
        0x4UL => double.MinValue,
        0x5UL => double.Epsilon,
        0x6UL => -double.Epsilon,
        0x7UL => 1.0,
        0x8UL => -1.0,
        0x9UL => 2.0,
        0xAUL => -2.0,
        0xBUL => 3.0,
        0xCUL => -3.0,
        0xDUL => 4.0,
        0xEUL => -4.0,
        _ => 0.0,
    };
    sealed class GenSpecial : Gen<double>
    {
        public override double Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64();
            size = new Size(i >> 12);
            return (i & 0xF0UL) == 0xD0UL ? MakeSpecial(i) : BitConverter.Int64BitsToDouble((long)i);
        }
    }
    /// <summary>With more special values like nan, inf, max, epsilon, -2, -1, 0, 1, 2.</summary>
    public readonly Gen<double> Special = new GenSpecial();
}

public sealed class GenDecimal : Gen<decimal>
{
    readonly static Gen<decimal> DefaultDecimal = Gen.Decimal[-1e25m, 1e25m];
    public override decimal Generate(PCG pcg, Size? min, out Size size)
        => DefaultDecimal.Generate(pcg, min, out size);
    sealed class GenEvenlyDistributed(decimal start, decimal length) : Gen<decimal>
    {
        public override decimal Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64() >> 12;
            size = new Size(i);
            return (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * length + start;
        }
    }
    private static Gen<decimal> EvenlyDistributed(decimal start, decimal finish)
    {
        finish -= start;
        return new GenEvenlyDistributed(start - finish, finish);
    }
    /// <summary>Generate decimal in the range <paramref name="start"/> to <paramref name="finish"/>.</summary>
    public Gen<decimal> this[decimal start, decimal finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            const int denominator = 99;
            const int minExp = -99;
            static Gen<int> GenInt(decimal start, decimal finish)
                => start <= int.MinValue && finish >= int.MaxValue ? Gen.Int
                 : Gen.Int[(int)Math.Max(Math.Ceiling(start), int.MinValue + 1), (int)Math.Min(Math.Floor(finish), int.MaxValue - 1)];
            var myGens = new (int, IGen<decimal>)[4];
            if (Math.Ceiling(start) <= Math.Floor(start))
                myGens[0] = (1, GenInt(start, finish).Select(i => (decimal)i));
            if (Math.Ceiling(start * denominator) <= Math.Floor(finish * denominator))
            {
                var lower = denominator - 1;
                while (Math.Ceiling(start * lower) <= Math.Floor(finish * lower) && lower > 1)
                    lower--;
                var rational = Gen.Int[lower + 1, denominator]
                    .SelectMany(den => GenInt(start * den, finish * den)
                    .Select(num => (decimal)num / den));
                myGens[1] = (1, rational);
            }
            Gen<decimal>? exponential = null;
            if (start <= 0 && finish >= 0)
            {
                var startExp = (int)Math.Ceiling(Math.Log10(Math.Abs((double)start))) - 3;
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs((double)finish))) - 3;
                if (startExp >= minExp && finishExp >= minExp)
                    exponential = Gen.OneOf(
                        Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => (decimal)Math.Pow(10, e) * m),
                        Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -(decimal)Math.Pow(10, e) * m));
                else if (startExp >= minExp)
                    exponential = Gen.Int[minExp, startExp].Select(Gen.Int9999, (e, m) => -(decimal)Math.Pow(10, e) * m);
                else if (finishExp >= minExp)
                    exponential = Gen.Int[minExp, finishExp].Select(Gen.Int9999, (e, m) => (decimal)Math.Pow(10, e) * m);
            }
            else if (start >= 0 && finish >= 0)
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs((double)start)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs((double)finish))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => (decimal)Math.Pow(10, e) * m);
            }
            else
            {
                var startExp = (int)Math.Floor(Math.Log10(Math.Abs((double)finish)));
                var finishExp = (int)Math.Ceiling(Math.Log10(Math.Abs((double)start))) - 3;
                if (finishExp > startExp + 3)
                    exponential = Gen.Int[startExp, finishExp].Select(Gen.Int9999, (e, m) => -(decimal)Math.Pow(10, e) * m);
            }
            if (exponential is not null)
                myGens[2] = (1, exponential.Where(r => r >= start && r <= finish));
            myGens[3] = (1, EvenlyDistributed(start, finish));
            return Gen.Frequency(myGens);
        }
    }
    sealed class GenUnit : Gen<decimal>
    {
        public override decimal Generate(PCG pcg, Size? min, out Size size)
        {
            ulong i = pcg.Next64() >> 12;
            size = new Size(i + 1UL);
            return (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1M;
        }
    }
    public Gen<decimal> Unit = new GenUnit();
}

/// <summary>Generate DateTime with DateTimeKind.Unspecified.</summary>
public sealed class GenDateTime : Gen<DateTime>
{
    const ulong max = 3155378975999999999UL; //(ulong)DateTime.MaxValue.Ticks;
    public override DateTime Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next64(max);
        size = new Size(i >> 10);
        return new DateTime((long)i);
    }
    sealed class Range(ulong start, ulong length) : Gen<DateTime>
    {
        public override DateTime Generate(PCG pcg, Size? min, out Size size)
        {
            ulong i = start + pcg.Next64(length);
            size = new Size(i);
            return new DateTime((long)i);
        }
    }
    /// <summary>Generate DateTime uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<DateTime> this[DateTime start, DateTime finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range((ulong)start.Ticks, (ulong)(finish.Ticks - start.Ticks + 1));
        }
    }

    sealed class GenDateTimeUtc : Gen<DateTime>
    {
        public override DateTime Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64(max);
            size = new Size(i >> 10);
            return new DateTime((long)i, DateTimeKind.Utc);
        }
        sealed class Range(ulong start, ulong length) : Gen<DateTime>
        {
            public override DateTime Generate(PCG pcg, Size? min, out Size size)
            {
                ulong i = start + pcg.Next64(length);
                size = new Size(i);
                return new DateTime((long)i, DateTimeKind.Utc);
            }
        }
        /// <summary>Generate DateTime uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
        public Gen<DateTime> this[DateTime start, DateTime finish]
        {
            get
            {
                if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
                return new Range((ulong)start.Ticks, (ulong)(finish.Ticks - start.Ticks + 1));
            }
        }
    }

    public readonly Gen<DateTime> Utc = new GenDateTimeUtc();

    sealed class GenDateTimeLocal : Gen<DateTime>
    {
        public override DateTime Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next64(max);
            size = new Size(i >> 10);
            return new DateTime((long)i, DateTimeKind.Local);
        }
        sealed class Range(ulong start, ulong length) : Gen<DateTime>
        {
            public override DateTime Generate(PCG pcg, Size? min, out Size size)
            {
                ulong i = start + pcg.Next64(length);
                size = new Size(i);
                return new DateTime((long)i, DateTimeKind.Local);
            }
        }
        /// <summary>Generate DateTime uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
        public Gen<DateTime> this[DateTime start, DateTime finish]
        {
            get
            {
                if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
                return new Range((ulong)start.Ticks, (ulong)(finish.Ticks - start.Ticks + 1));
            }
        }
    }

    public readonly Gen<DateTime> Local = new GenDateTimeLocal();
}

public sealed class GenDate : Gen<DateTime>
{
    const uint max = 3652059U; //(uint)(DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay);
    public override DateTime Generate(PCG pcg, Size? min, out Size size)
    {
        uint i = pcg.Next(max);
        size = new Size(i);
        return new DateTime(i * TimeSpan.TicksPerDay);
    }
    sealed class Range(uint s, uint l) : Gen<DateTime>
    {
        public override DateTime Generate(PCG pcg, Size? min, out Size size)
        {
            var i = s + pcg.Next(l);
            size = new Size(i);
            return new DateTime(i * TimeSpan.TicksPerDay);
        }
    }

    /// <summary>Generate Date uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<DateTime> this[DateTime start, DateTime finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range((uint)(start.Ticks / TimeSpan.TicksPerDay), (uint)((finish.Ticks - start.Ticks) / TimeSpan.TicksPerDay) + 1U);
        }
    }
}

public sealed class GenDateOnly : Gen<DateOnly>
{
    const uint max = 3652059U;
    public override DateOnly Generate(PCG pcg, Size? min, out Size size)
    {
        uint i = pcg.Next(max);
        size = new Size(i);
        return DateOnly.FromDayNumber((int)i);
    }
    sealed class Range(uint s, uint l) : Gen<DateOnly>
    {
        public override DateOnly Generate(PCG pcg, Size? min, out Size size)
        {
            var i = s + pcg.Next(l);
            size = new Size(i);
            return DateOnly.FromDayNumber((int)i);
        }
    }

    /// <summary>Generate DateOnly uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<DateOnly> this[DateOnly start, DateOnly finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range((uint)start.GetHashCode(), (uint)(finish.GetHashCode() - start.GetHashCode() + 1));
        }
    }
}

public sealed class GenTimeOnly : Gen<TimeOnly>
{
    const ulong max = 864_000_000_000;
    public override TimeOnly Generate(PCG pcg, Size? min, out Size size)
    {
        var i = pcg.Next64(max);
        size = new Size(i);
        return new TimeOnly((long)i);
    }
    sealed class Range(ulong s, ulong l) : Gen<TimeOnly>
    {
        public override TimeOnly Generate(PCG pcg, Size? min, out Size size)
        {
            var i = s + pcg.Next64(l);
            size = new Size(i);
            return new TimeOnly((long)i);
        }
    }

    /// <summary>Generate TimeOnly uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<TimeOnly> this[TimeOnly start, TimeOnly finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range((ulong)start.Ticks, (ulong)(finish.Ticks - start.Ticks + 1));
        }
    }
}

public sealed class GenTimeSpan : Gen<TimeSpan>
{
    public override TimeSpan Generate(PCG pcg, Size? min, out Size size)
    {
        ulong i = pcg.Next64();
        size = new Size(i >> 12);
        return new TimeSpan((long)i);
    }
    sealed class Range(ulong start, ulong length) : Gen<TimeSpan>
    {
        public override TimeSpan Generate(PCG pcg, Size? min, out Size size)
        {
            var i = start + pcg.Next64(length);
            size = new Size(i);
            return new TimeSpan((long)i);
        }
    }
    /// <summary>Generate TimeSpan uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<TimeSpan> this[TimeSpan start, TimeSpan finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new Range((ulong)start.Ticks, (ulong)(finish.Ticks - start.Ticks + 1));
        }
    }
}

public sealed class GenDateTimeOffset : Gen<DateTimeOffset>
{
    readonly Gen<DateTime> genDateTime = Gen.DateTime[new DateTime(1800, 1, 1), new DateTime(2200, 1, 1)];
    readonly Gen<int> genOffset = Gen.Int[-14 * 60, 14 * 60];
    public override DateTimeOffset Generate(PCG pcg, Size? min, out Size size)
    {
        var os = genOffset.Generate(pcg, null, out Size s1);
        var dt = genDateTime.Generate(pcg, null, out Size s2);
        size = new Size(s1.I, s2);
        return new DateTimeOffset(dt, TimeSpan.FromMinutes(os));
    }
}

public sealed class GenGuid : Gen<Guid>
{
    [StructLayout(LayoutKind.Explicit)]
    struct GuidConverter
    {
        [FieldOffset(0)] public Guid G;
        [FieldOffset(0)] public uint I0;
        [FieldOffset(4)] public uint I1;
        [FieldOffset(8)] public uint I2;
        [FieldOffset(12)] public uint I3;
    }
    public override Guid Generate(PCG pcg, Size? min, out Size size)
    {
        var c = new GuidConverter { I0 = pcg.Next(), I1 = pcg.Next(), I2 = pcg.Next(), I3 = pcg.Next() };
        size = new Size(c.I0 + c.I1 + c.I2 + c.I3 + 1UL);
        return c.G;
    }
}

public sealed class GenChar : Gen<char>
{
    static readonly Gen<char> StandardChar = Gen.Char[' ', '~'];
    public override char Generate(PCG pcg, Size? min, out Size size)
        => StandardChar.Generate(pcg, min, out size);
    sealed class Range(uint start, uint length) : Gen<char>
    {
        public override char Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next(length);
            size = new Size(i);
            return (char)(start + i);
        }
    }

    /// <summary>Generate char uniformly distributed in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<char> this[char start, char finish]
        => new Range(start, finish + 1U - start);
    sealed class GenChars(string chars) : Gen<char>
    {
        public override char Generate(PCG pcg, Size? min, out Size size)
        {
            var i = pcg.Next((uint)chars.Length);
            size = new Size(i);
            return chars[(int)i];
        }
    }
    /// <summary>Generate char from chars in the string.</summary>
    public Gen<char> this[string chars] => new GenChars(chars);
    public readonly Gen<char> AlphaNumeric = new GenChars("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");
}

public sealed class GenString : Gen<string>
{
    static readonly Gen<string> d = Gen.Char.Array.Select(i => new string(i));
    public override string Generate(PCG pcg, Size? min, out Size size)
        => d.Generate(pcg, min, out size);
    /// <summary>Generate string with length in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<string> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return Gen.Char.Array[start, finish].Select(i => new string(i));
        }
    }

    public Gen<string> this[Gen<char> gen, int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return gen.Array[start, finish].Select(i => new string(i));
        }
    }

    public Gen<string> this[Gen<char> gen] =>
        gen.Array.Select(i => new string(i));
    /// <summary>Generate string from chars in the string.</summary>
    public Gen<string> this[string chars] =>
        Gen.Char[chars].Array.Select(i => new string(i));
    public readonly Gen<string> AlphaNumeric = Gen.Char.AlphaNumeric.Array.Select(i => new string(i));
}

public sealed class GenSeed : Gen<string>
{
    public override string Generate(PCG pcg, Size? min, out Size size)
    {
        size = new Size(0);
        return new PCG(pcg.Next(), pcg.Next64()).ToString();
    }
}

public sealed class GenArray<T>(Gen<T> gen) : Gen<T[]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static T[] Generate(Gen<T> gen, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var next = sizeI == min?.I ? min.Next : null;
        var vs = new T[length];
        for (int i = 0; i < vs.Length; i++)
        {
            vs[i] = gen.Generate(pcg, next, out var si);
            total.Add(si);
            if (Size.IsLessThan(min, size)) return default!;
        }
        return vs;
    }
    public override T[] Generate(PCG pcg, Size? min, out Size size)
        => Generate(gen, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<T> gen, Gen<int> length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
            => GenArray<T>.Generate(gen, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<T[]> this[Gen<int> length] => new GenLength(gen, length);
    /// <summary>Generate an array with length in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<T[]> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(gen, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<T> gen, int length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
            => GenArray<T>.Generate(gen, pcg, min, length, out size);
    }
    /// <summary>Generate an array of fixed length.</summary>
    public Gen<T[]> this[int length] => new FixedLength(gen, length);
    sealed class GenNonempty(Gen<T> gen) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            var length = (int)(pcg.Next() & 127U) + 1;
            return GenArray<T>.Generate(gen, pcg, min, length, out size);
        }
    }
    /// <summary>Generate a non-empty array of length in the range 1 to 128 inclusive.</summary>
    public Gen<T[]> Nonempty => new GenNonempty(gen);
}

public sealed class GenArrayUnique<T>(Gen<T> gen) : Gen<T[]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static T[] Generate(Gen<T> gen, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var hs = new HashSet<T>();
        var vs = new T[length];
        int i = 0;
        var bad = 0;
        while (i < length)
        {
            var v = gen.Generate(pcg, null, out var s);
            if (hs.Add(v))
            {
                vs[i++] = v;
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                bad = 0;
            }
            else if (++bad == 1000)
            {
                ThrowHelper.Throw("Failing to add to ArrayUnique");
            }
        }
        return vs;
    }
    public override T[] Generate(PCG pcg, Size? min, out Size size)
        => Generate(gen, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<T> gen, Gen<int> length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
            => GenArrayUnique<T>.Generate(gen, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<T[]> this[Gen<int> length] => new GenLength(gen, length);
    /// <summary>Generate a unique array with length in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<T[]> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(gen, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<T> gen, int length) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
            => GenArrayUnique<T>.Generate(gen, pcg, min, length, out size);
    }
    /// <summary>Generate a unique array of fixed length.</summary>
    public Gen<T[]> this[int length] => new FixedLength(gen, length);
    sealed class GenNonempty(Gen<T> gen) : Gen<T[]>
    {
        public override T[] Generate(PCG pcg, Size? min, out Size size)
        {
            var length = (int)(pcg.Next() & 127U) + 1;
            return GenArrayUnique<T>.Generate(gen, pcg, min, length, out size);
        }
    }
    /// <summary>Generate a unique non-empty array of length in the range 1 to 128 inclusive.</summary>
    public Gen<T[]> Nonempty => new GenNonempty(gen);
}

public sealed class GenArray2D<T>(Gen<T> gen) : Gen<T[,]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static T[,] Generate(Gen<T> gen, PCG pcg, Size? min, int length0, int length1, out Size size)
    {
        var sizeI = ((ulong)(length0 * length1)) << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var next = sizeI == min?.I ? min.Next : null;
        var vs = new T[length0, length1];
        for (int i = 0; i < length0; i++)
        {
            for (int j = 0; j < length1; j++)
            {
                vs[i, j] = gen.Generate(pcg, next, out var s);
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
            }
        }
        return vs;
    }
    public override T[,] Generate(PCG pcg, Size? min, out Size size)
        => Generate(gen, pcg, min, (int)(pcg.Next() & 127U), (int)(pcg.Next() & 127U), out size);
    sealed class FixedLength(Gen<T> gen, int length0, int length1) : Gen<T[,]>
    {
        public override T[,] Generate(PCG pcg, Size? min, out Size size)
            => GenArray2D<T>.Generate(gen, pcg, min, length0, length1, out size);
    }
    public Gen<T[,]> this[int length0, int length1] => new FixedLength(gen, length0, length1);
    sealed class GenLength(Gen<T> gen, Gen<int> length0, Gen<int> length1) : Gen<T[,]>
    {
        public override T[,] Generate(PCG pcg, Size? min, out Size size)
            => GenArray2D<T>.Generate(gen, pcg, min, length0.Generate(pcg, null, out _), length1.Generate(pcg, null, out _), out size);
    }
    public Gen<T[,]> this[Gen<int> length0, Gen<int> length1] => new GenLength(gen, length0, length1);
    sealed class GenNonempty(Gen<T> gen) : Gen<T[,]>
    {
        public override T[,] Generate(PCG pcg, Size? min, out Size size)
            => GenArray2D<T>.Generate(gen, pcg, min, (int)(pcg.Next() & 127U) + 1, (int)(pcg.Next() & 127U) + 1, out size);
    }
    /// <summary>Generate a non-empty 2d array of lengths in the range 1 to 128 inclusive.</summary>
    public Gen<T[,]> Nonempty => new GenNonempty(gen);
}

public sealed class GenList<T>(Gen<T> gen) : Gen<List<T>>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static List<T> Generate(Gen<T> gen, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var next = sizeI == min?.I ? min.Next : null;
        var vs = new List<T>(length);
        for (int i = 0; i < length; i++)
        {
            vs.Add(gen.Generate(pcg, next, out var s));
            total.Add(s);
            if (Size.IsLessThan(min, size)) return default!;
        }
        return vs;
    }
    public override List<T> Generate(PCG pcg, Size? min, out Size size)
        => Generate(gen, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<T> gen, Gen<int> length) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
            => GenList<T>.Generate(gen, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<List<T>> this[Gen<int> length] => new GenLength(gen, length);
    /// <summary>Generate a List with Count in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<List<T>> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(gen, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<T> gen, int length) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
            => GenList<T>.Generate(gen, pcg, min, length, out size);
    }
    /// <summary>Generate a List of fixed length.</summary>
    public Gen<List<T>> this[int length] => new FixedLength(gen, length);
    sealed class GenNonempty(Gen<T> gen) : Gen<List<T>>
    {
        public override List<T> Generate(PCG pcg, Size? min, out Size size)
            => GenList<T>.Generate(gen, pcg, min, (int)(pcg.Next() & 127U) + 1, out size);
    }
    /// <summary>Generate a non-empty List of length in the range 1 to 128 inclusive.</summary>
    public Gen<List<T>> Nonempty => new GenNonempty(gen);
}

public sealed class GenHashSet<T>(Gen<T> gen) : Gen<HashSet<T>>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static HashSet<T> Generate(Gen<T> gen, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var vs = new HashSet<T>();
        var bad = 0;
        while (length > 0)
        {
            if (vs.Add(gen.Generate(pcg, null, out var s)))
            {
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                length--;
                bad = 0;
            }
            else if (++bad == 1000)
            {
                ThrowHelper.Throw("Failing to add to HashSet");
            }
        }
        return vs;
    }
    public override HashSet<T> Generate(PCG pcg, Size? min, out Size size)
        => Generate(gen, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<T> gen, Gen<int> length) : Gen<HashSet<T>>
    {
        public override HashSet<T> Generate(PCG pcg, Size? min, out Size size)
            => GenHashSet<T>.Generate(gen, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<HashSet<T>> this[Gen<int> length] => new GenLength(gen, length);
    /// <summary>Generate a HashSet with Count in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<HashSet<T>> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(gen, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<T> gen, int length) : Gen<HashSet<T>>
    {
        public override HashSet<T> Generate(PCG pcg, Size? min, out Size size)
            => GenHashSet<T>.Generate(gen, pcg, min, length, out size);
    }
    /// <summary>Generate a HashSet of fixed length.</summary>
    public Gen<HashSet<T>> this[int length] => new FixedLength(gen, length);
    sealed class GenNonempty(Gen<T> gen) : Gen<HashSet<T>>
    {
        public override HashSet<T> Generate(PCG pcg, Size? min, out Size size)
            => GenHashSet<T>.Generate(gen, pcg, min, (int)(pcg.Next() & 127U) + 1, out size);
    }
    /// <summary>Generate a non-empty HashSet of length in the range 1 to 128 inclusive.</summary>
    public Gen<HashSet<T>> Nonempty => new GenNonempty(gen);
}

public sealed class GenDictionary<K, V>(Gen<K> genK, Gen<V> genV) : Gen<Dictionary<K, V>> where K : notnull
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Dictionary<K, V> Generate(Gen<K> genK, Gen<V> genV, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var next = sizeI == min?.I ? min.Next : null;
        var vs = new Dictionary<K, V>(length);
        var i = length;
        var bad = 0;
        while (i > 0)
        {
            var k = genK.Generate(pcg, null, out var s);
            if (!vs.ContainsKey(k))
            {
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                var v = genV.Generate(pcg, next, out s);
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                vs.Add(k, v);
                i--;
                bad = 0;
            }
            else if (++bad == 1000)
            {
                ThrowHelper.Throw("Failing to add to Dictionary");
            }
        }
        return vs;
    }
    public override Dictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
        => Generate(genK, genV, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<K> genK, Gen<V> genV, Gen<int> length) : Gen<Dictionary<K, V>>
    {
        public override Dictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenDictionary<K, V>.Generate(genK, genV, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<Dictionary<K, V>> this[Gen<int> length] => new GenLength(genK, genV, length);
    /// <summary>Generate a Dictionary with Count in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<Dictionary<K, V>> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(genK, genV, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<K> genK, Gen<V> genV, int length) : Gen<Dictionary<K, V>>
    {
        public override Dictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenDictionary<K, V>.Generate(genK, genV, pcg, min, length, out size);
    }
    /// <summary>Generate a Dictionary of fixed length.</summary>
    public Gen<Dictionary<K, V>> this[int length] => new FixedLength(genK, genV, length);
    sealed class GenNonempty(Gen<K> genK, Gen<V> genV) : Gen<Dictionary<K, V>>
    {
        public override Dictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenDictionary<K, V>.Generate(genK, genV, pcg, min, (int)(pcg.Next() & 127U) + 1, out size);
    }
    /// <summary>Generate a non-empty Dictionary of length in the range 1 to 128 inclusive.</summary>
    public Gen<Dictionary<K, V>> Nonempty => new GenNonempty(genK, genV);
}

public sealed class GenSortedDictionary<K, V>(Gen<K> genK, Gen<V> genV) : Gen<SortedDictionary<K, V>> where K : notnull
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static SortedDictionary<K, V> Generate(Gen<K> genK, Gen<V> genV, PCG pcg, Size? min, int length, out Size size)
    {
        var sizeI = (ulong)length << 32;
        var total = new Size(0);
        size = new Size(sizeI, total);
        if (min?.I < sizeI) return default!;
        var next = sizeI == min?.I ? min.Next : null;
        var vs = new SortedDictionary<K, V>();
        var i = length;
        var bad = 0;
        while (i > 0)
        {
            var k = genK.Generate(pcg, null, out var s);
            if (!vs.ContainsKey(k))
            {
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                var v = genV.Generate(pcg, next, out s);
                total.Add(s);
                if (Size.IsLessThan(min, size)) return default!;
                vs.Add(k, v);
                i--;
                bad = 0;
            }
            else if (++bad == 1000)
            {
                ThrowHelper.Throw("Failing to add to SortedDictionary");
            }
        }
        return vs;
    }
    public override SortedDictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
        => Generate(genK, genV, pcg, min, (int)(pcg.Next() & 127U), out size);
    sealed class GenLength(Gen<K> genK, Gen<V> genV, Gen<int> length) : Gen<SortedDictionary<K, V>>
    {
        public override SortedDictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenSortedDictionary<K, V>.Generate(genK, genV, pcg, min, length.Generate(pcg, null, out _), out size);
    }
    public Gen<SortedDictionary<K, V>> this[Gen<int> length] => new GenLength(genK, genV, length);
    /// <summary>Generate a SortedDictionary with Count in the range <paramref name="start"/> to <paramref name="finish"/> both inclusive.</summary>
    public Gen<SortedDictionary<K, V>> this[int start, int finish]
    {
        get
        {
            if (finish < start) ThrowHelper.ThrowFinishLessThanStart(start, finish);
            return new GenLength(genK, genV, Gen.Int[start, finish]);
        }
    }

    sealed class FixedLength(Gen<K> genK, Gen<V> genV, int length) : Gen<SortedDictionary<K, V>>
    {
        public override SortedDictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenSortedDictionary<K, V>.Generate(genK, genV, pcg, min, length, out size);
    }
    /// <summary>Generate a SortedDictionary of fixed length.</summary>
    public Gen<SortedDictionary<K, V>> this[int length] => new FixedLength(genK, genV, length);
    sealed class GenNonempty(Gen<K> genK, Gen<V> genV) : Gen<SortedDictionary<K, V>>
    {
        public override SortedDictionary<K, V> Generate(PCG pcg, Size? min, out Size size)
            => GenSortedDictionary<K, V>.Generate(genK, genV, pcg, min, (int)(pcg.Next() & 127U) + 1, out size);
    }
    /// <summary>Generate a non-empty SortedDictionary of length in the range 1 to 128 inclusive.</summary>
    public Gen<SortedDictionary<K, V>> Nonempty => new GenNonempty(genK, genV);
}

public sealed class GenOperation<T> : Gen<(string, Action<T>)>
{
    public bool AddOpNumber;
    readonly Gen<(string, Action<T>)> gen;
    internal GenOperation(Gen<(string, Action<T>)> gen, bool addOpNumber)
    {
        this.gen = gen;
        AddOpNumber = addOpNumber;
    }
    public override (string, Action<T>) Generate(PCG pcg, Size? min, out Size size) => gen.Generate(pcg, min, out size);
}

public sealed class GenOperation<Actual, Model> : Gen<(string, Action<Actual>, Action<Model>)>
{
    readonly Gen<(string, Action<Actual>, Action<Model>)> gen;
    public bool AddOpNumber;
    internal GenOperation(Gen<(string, Action<Actual>, Action<Model>)> gen, bool addOpNumber)
    {
        this.gen = gen;
        AddOpNumber = addOpNumber;
    }
    public override (string, Action<Actual>, Action<Model>) Generate(PCG pcg, Size? min, out Size size) => gen.Generate(pcg, min, out size);
}

public sealed class GenMetamorphic<T> : Gen<(string, Action<T>, Action<T>)>
{
    readonly Gen<(string, Action<T>, Action<T>)> gen;
    internal GenMetamorphic(Gen<(string, Action<T>, Action<T>)> gen) => this.gen = gen;
    public override (string, Action<T>, Action<T>) Generate(PCG pcg, Size? min, out Size size) => gen.Generate(pcg, min, out size);
}

public static class GenOperation
{
    public static GenOperation<S> Create<S, T>(Gen<T> gen, Action<S, T> action) =>
        new(gen.Select<T, (string, Action<S>)>(t => (" " + Check.Print(t), s => action(s, t))), true);
    public static GenOperation<S> Create<S, T>(Gen<T> gen, Func<T, string> name, Action<S, T> action) =>
        new(gen.Select<T, (string, Action<S>)>(t => (name(t), s => action(s, t))), false);
    public static GenOperation<Actual, Model> Create<Actual, Model, T>(Gen<T> gen, Action<Actual, T> actual, Action<Model, T> model) =>
        new(gen.Select<T, (string, Action<Actual>, Action<Model>)>(t => (" " + Check.Print(t), a => actual(a, t), m => model(m, t))), true);
    public static GenOperation<Actual, Model> Create<Actual, Model, T>(Gen<T> gen, Func<T, string> name, Action<Actual, T> actual, Action<Model, T> model) =>
        new(gen.Select<T, (string, Action<Actual>, Action<Model>)>(t => (name(t), a => actual(a, t), m => model(m, t))), false);
    public static GenOperation<Actual, Model> Create<Actual, Model>(Action<Actual> actual, Action<Model> model)
        => new(Gen.Const(("", actual, model)), true);
    public static GenOperation<Actual, Model> Create<Actual, Model>(string name, Action<Actual> actual, Action<Model> model)
        => new(Gen.Const((name, actual, model)), false);
    public static GenOperation<T> Create<T>(Action<T> action)
        => new(Gen.Const(("", action)), true);
    public static GenOperation<T> Create<T>(string name, Action<T> action)
        => new(Gen.Const((name, action)), false);
}

public static class GenMetamorphic
{
    public static GenMetamorphic<S> Create<S, T>(Gen<T> gen, Func<T, string> name, Action<S, T> action1, Action<S, T> action2) =>
        new(gen.Select<T, (string, Action<S>, Action<S>)>(t => (name(t), s => action1(s, t), s => action2(s, t))));
}