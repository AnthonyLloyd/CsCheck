// Copyright 2022 Anthony Lloyd
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
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

/// <summary>Size representation of Gen generated data.</summary>
public class Size
{
    public ulong I;
    public Size Next;

    public Size(ulong i) => I = i;

    public Size(ulong i, Size next)
    {
        I = i;
        Next = next;
    }

    public void Add(Size a)
    {
        I += a.I;
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

    public static bool IsLessThan(Size s1, Size s2) => s1 is not null && (s2 is null || s1.I < s2.I || (s1.I == s2.I && IsLessThan(s1.Next, s2.Next)));
}

public interface IGen<out T>
{
    T Generate(PCG pcg, Size min, out Size size);
}

/// <summary>Data and Size generator from a PCG random number generator.</summary>
public abstract class Gen<T> : IGen<T>
{
    public abstract T Generate(PCG pcg, Size min, out Size size);
    public Gen<R> Cast<R>() => Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        var o = Generate(pcg, min, out size);
        return o is R t ? t : (R)Convert.ChangeType(o, typeof(R));
    });

    public GenOperation<S> Operation<S>(Func<T, string> name, Action<S, T> action) => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (name(t), m => action(m, t));
    });

    public GenOperation<S> Operation<S>(Action<S, T> action) => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (" " + Check.Print(t), m => action(m, t));
    }, true);

    public GenOperation<Actual, Model> Operation<Actual, Model>(Func<T, string> name, Action<Actual, Model, T> action)
        => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (name(t), (a, m) => action(a, m, t));
    });

    public GenOperation<Actual, Model> Operation<Actual, Model>(Action<Actual, Model, T> action)
        => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (" " + Check.Print(t), (a, m) => action(a, m, t));
    }, true);

    public GenMetamorphic<S> Metamorphic<S>(Func<T, string> name, Action<S, T> action1, Action<S, T> action2) => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (name(t), m => action1(m, t), m => action2(m, t));
    });

    public GenMetamorphic<S> Metamorphic<S>(Action<S, T> action1, Action<S, T> action2) => new((PCG pcg, Size min, out Size size) =>
    {
        var t = Generate(pcg, min, out size);
        return (Check.Print(t), m => action1(m, t), m => action2(m, t));
    });

    public GenArray<T> Array => new(this);
    public GenEnumerable<T> Enumerable => new(this);
    public GenArray2D<T> Array2D => new(this);
    public GenList<T> List => new(this);
    public GenHashSet<T> HashSet => new(this);
    public GenArrayUnique<T> ArrayUnique => new(this);
}

public delegate T GenDelegate<out T>(PCG pcg, Size min, out Size size);
public delegate T GenMap<T>(T pcg, ref Size size);

/// <summary>Provides a set of static methods for composing generators.</summary>
public static class Gen
{
    class GenCreate<T> : Gen<T>
    {
        readonly GenDelegate<T> generate;
        internal GenCreate(GenDelegate<T> generate) => this.generate = generate;
        public override T Generate(PCG pcg, Size min, out Size size) => generate(pcg, min, out size);
    }

    public static Gen<T> Create<T>(GenDelegate<T> gen) => new GenCreate<T>(gen);

    public static Gen<T> Create<T>(Func<PCG, (T, Size)> gen) => new GenCreate<T>((PCG pcg, Size min, out Size size) =>
      {
          T t;
          (t, size) = gen(pcg);
          return t;
      });

    public static Gen<R> Select<T, R>(this Gen<T> gen, Func<T, R> selector) =>
        Create((PCG pcg, Size min, out Size size) => selector(gen.Generate(pcg, min, out size)));

    public static Gen<R> Select<T1, T2, R>(this Gen<(T1, T2)> gen, Func<T1, T2, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2) = gen.Generate(pcg, min, out size);
            return selector(t1, t2);
        });

    public static Gen<R> Select<T1, T2, T3, R>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3);
        });

    public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3, t4) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3, t4);
        });

    public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3, t4, t5) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3, t4, t5);
        });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3, t4, t5, t6) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3, t4, t5, t6);
        });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3, t4, t5, t6, t7) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3, t4, t5, t6, t7);
        });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
        {
            var (t1, t2, t3, t4, t5, t6, t7, t8) = gen.Generate(pcg, min, out size);
            return selector(t1, t2, t3, t4, t5, t6, t7, t8);
        });

    public static Gen<R> Select<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2,
        Func<T1, T2, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x2_0000_0000UL) { size = new Size(0x2_0000_0000UL); return default; }
            min = min.I == 0x2_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        size = new Size(0x2_0000_0000UL, size);
        return selector(v1, v2);
    });

    public static Gen<R> Select<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Func<T1, T2, T3, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x3_0000_0000UL) { size = new Size(0x3_0000_0000UL); return default; }
            min = min.I == 0x3_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x3_0000_0000UL, size);
        return selector(v1, v2, v3);
    });

    public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Func<T1, T2, T3, T4, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x4_0000_0000UL) { size = new Size(0x4_0000_0000UL); return default; }
            min = min.I == 0x4_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x4_0000_0000UL, size);
        return selector(v1, v2, v3, v4);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Func<T1, T2, T3, T4, T5, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x5_0000_0000UL) { size = new Size(0x5_0000_0000UL); return default; }
            min = min.I == 0x5_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x5_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x6_0000_0000UL) { size = new Size(0x6_0000_0000UL); return default; }
            min = min.I == 0x6_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x6_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) =>
        Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x7_0000_0000UL) { size = new Size(0x7_0000_0000UL); return default; }
            min = min.I == 0x7_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x7_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector)
        => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x8_0000_0000UL) { size = new Size(0x8_0000_0000UL); return default; }
            min = min.I == 0x8_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x8_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x9_0000_0000UL) { size = new Size(0x9_0000_0000UL); return default; }
            min = min.I == 0x9_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x9_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xA_0000_0000UL) { size = new Size(0xA_0000_0000UL); return default; }
            min = min.I == 0xA_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xA_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10, Gen<T11> gen11,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xB_0000_0000UL) { size = new Size(0xB_0000_0000UL); return default; }
            min = min.I == 0xB_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xB_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10, Gen<T11> gen11, Gen<T12> gen12,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xC_0000_0000UL) { size = new Size(0xC_0000_0000UL); return default; }
            min = min.I == 0xC_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        var v12 = gen12.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xC_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, R>(this Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
        Gen<T11> gen11, Gen<T12> gen12, Gen<T13> gen13,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xD_0000_0000UL) { size = new Size(0xD_0000_0000UL); return default; }
            min = min.I == 0xD_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        var v12 = gen12.Generate(pcg, min, out s);
        size.Add(s);
        var v13 = gen13.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xD_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, R>(this Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
        Gen<T11> gen11, Gen<T12> gen12, Gen<T13> gen13, Gen<T14> gen14,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xE_0000_0000UL) { size = new Size(0xE_0000_0000UL); return default; }
            min = min.I == 0xE_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        var v12 = gen12.Generate(pcg, min, out s);
        size.Add(s);
        var v13 = gen13.Generate(pcg, min, out s);
        size.Add(s);
        var v14 = gen14.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xE_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, R>(this Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
        Gen<T11> gen11, Gen<T12> gen12, Gen<T13> gen13, Gen<T14> gen14, Gen<T15> gen15,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0xF_0000_0000UL) { size = new Size(0xF_0000_0000UL); return default; }
            min = min.I == 0xF_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        var v12 = gen12.Generate(pcg, min, out s);
        size.Add(s);
        var v13 = gen13.Generate(pcg, min, out s);
        size.Add(s);
        var v14 = gen14.Generate(pcg, min, out s);
        size.Add(s);
        var v15 = gen15.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0xF_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15);
    });

    public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, R>(this Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
        Gen<T11> gen11, Gen<T12> gen12, Gen<T13> gen13, Gen<T14> gen14, Gen<T15> gen15, Gen<T16> gen16,
    Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, R> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x10_0000_0000UL) { size = new Size(0x10_0000_0000UL); return default; }
            min = min.I == 0x10_0000_0000UL ? min.Next : null;
        }
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, min, out s);
        size.Add(s);
        var v6 = gen6.Generate(pcg, min, out s);
        size.Add(s);
        var v7 = gen7.Generate(pcg, min, out s);
        size.Add(s);
        var v8 = gen8.Generate(pcg, min, out s);
        size.Add(s);
        var v9 = gen9.Generate(pcg, min, out s);
        size.Add(s);
        var v10 = gen10.Generate(pcg, min, out s);
        size.Add(s);
        var v11 = gen11.Generate(pcg, min, out s);
        size.Add(s);
        var v12 = gen12.Generate(pcg, min, out s);
        size.Add(s);
        var v13 = gen13.Generate(pcg, min, out s);
        size.Add(s);
        var v14 = gen14.Generate(pcg, min, out s);
        size.Add(s);
        var v15 = gen15.Generate(pcg, min, out s);
        size.Add(s);
        var v16 = gen16.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x10_0000_0000UL, size);
        return selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15, v16);
    });

    public static Gen<(T0 V0, T1 V1)> Select<T0, T1>(this Gen<T0> gen0, Gen<T1> gen1) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x2_0000_0000UL) { size = new Size(0x2_0000_0000UL); return default; }
            min = min.I == 0x2_0000_0000UL ? min.Next : null;
        }
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        size = new Size(0x2_0000_0000UL, size);
        return (v0, v1);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2)> Select<T0, T1, T2>(this Gen<T0> gen0, Gen<T1> gen1, Gen<T2> gen2)
        => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x3_0000_0000UL) { size = new Size(0x3_0000_0000UL); return default; }
            min = min.I == 0x3_0000_0000UL ? min.Next : null;
        }
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        var v2 = gen2.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x3_0000_0000UL, size);
        return (v0, v1, v2);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3)> Select<T0, T1, T2, T3>(this Gen<T0> gen0, Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x4_0000_0000UL) { size = new Size(0x4_0000_0000UL); return default; }
            min = min.I == 0x4_0000_0000UL ? min.Next : null;
        }
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        var v2 = gen2.Generate(pcg, min, out s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x4_0000_0000UL, size);
        return (v0, v1, v2, v3);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3, T4 V4)> Select<T0, T1, T2, T3, T4>(this Gen<T0> gen0, Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x5_0000_0000UL) { size = new Size(0x5_0000_0000UL); return default; }
            min = min.I == 0x5_0000_0000UL ? min.Next : null;
        }
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        var v2 = gen2.Generate(pcg, min, out s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, min, out s);
        size.Add(s);
        size = new Size(0x5_0000_0000UL, size);
        return (v0, v1, v2, v3, v4);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3, T4 V4, T5 V5)> Select<T0, T1, T2, T3, T4, T5>(this Gen<T0> gen0, Gen<T1> gen1,
        Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4, Gen<T5> gen5) => Create((PCG pcg, Size min, out Size size) =>
        {
            if (min is not null)
            {
                if (min.I < 0x6_0000_0000UL) { size = new Size(0x6_0000_0000UL); return default; }
                min = min.I == 0x6_0000_0000UL ? min.Next : null;
            }
            var v0 = gen0.Generate(pcg, min, out size);
            var v1 = gen1.Generate(pcg, min, out var s);
            size.Add(s);
            var v2 = gen2.Generate(pcg, min, out s);
            size.Add(s);
            var v3 = gen3.Generate(pcg, min, out s);
            size.Add(s);
            var v4 = gen4.Generate(pcg, min, out s);
            size.Add(s);
            var v5 = gen5.Generate(pcg, min, out s);
            size.Add(s);
            size = new Size(0x6_0000_0000UL, size);
            return (v0, v1, v2, v3, v4, v5);
        });

    public static Gen<(T0 V0, T1 V1)> SelectTuple<T0, T1>(this Gen<T0> gen, Func<T0, Gen<T1>> selector)
        => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen.Generate(pcg, min, out size);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1).Generate(pcg, null, out Size sR);
        size.Append(sR);
        return (v1, vR);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2)> SelectTuple<T0, T1, T2>(this Gen<T0> gen0, Gen<T1> gen1,
        Func<T0, T1, Gen<T2>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var v2 = selector(v0, v1).Generate(pcg, null, out s);
        size.Append(s);
        return (v0, v1, v2);
    });

    public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3)> SelectTuple<T0, T1, T2, T3>(this Gen<T0> gen0, Gen<T1> gen1, Gen<T2> gen2,
        Func<T0, T1, T2, Gen<T3>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v0 = gen0.Generate(pcg, min, out size);
        var v1 = gen1.Generate(pcg, min, out var s);
        size.Add(s);
        var v2 = gen2.Generate(pcg, min, out s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var v3 = selector(v0, v1, v2).Generate(pcg, null, out s);
        size.Append(s);
        return (v0, v1, v2, v3);
    });

    public static Gen<R> SelectMany<T, R>(this Gen<T> gen, Func<T, IGen<R>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen.Generate(pcg, min, out size);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1).Generate(pcg, null, out var sR);
        size.Append(sR);
        return vR;
    });

    public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, Gen<R>> selector)
        => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1, v2).Generate(pcg, null, out s);
        size.Append(s);
        return vR;
    });

    public static Gen<R> SelectMany<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
        Func<T1, T2, T3, Gen<R>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen1.Generate(pcg, min, out size);
        var v2 = gen2.Generate(pcg, min, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, min, out s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1, v2, v3).Generate(pcg, null, out s);
        size.Append(s);
        return vR;
    });

    public static Gen<R> SelectMany<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Func<T1, T2, T3, T4, Gen<R>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen1.Generate(pcg, null, out size);
        var v2 = gen2.Generate(pcg, null, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, null, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, null, out s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1, v2, v3, v4).Generate(pcg, null, out s);
        size.Append(s);
        return vR;
    });

    public static Gen<R> SelectMany<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
        Gen<T5> gen5, Func<T1, T2, T3, T4, T5, Gen<R>> selector) => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen1.Generate(pcg, null, out size);
        var v2 = gen2.Generate(pcg, null, out var s);
        size.Add(s);
        var v3 = gen3.Generate(pcg, null, out s);
        size.Add(s);
        var v4 = gen4.Generate(pcg, null, out s);
        size.Add(s);
        var v5 = gen5.Generate(pcg, null, out s);
        size.Add(s);
        if (min is not null && min.I < size.I) return default;
        var vR = selector(v1, v2, v3, v4, v5).Generate(pcg, null, out s);
        size.Append(s);
        return vR;
    });

    public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Func<T1, Gen<T2>> genSelector, Func<T1, T2, R> resultSelector)
        => Create((PCG pcg, Size min, out Size size) =>
    {
        var v1 = gen1.Generate(pcg, null, out size);
        if (min is not null && min.I < size.I) return default;
        var v2 = genSelector(v1).Generate(pcg, null, out var s);
        size.Append(s);
        return resultSelector(v1, v2);
    });

    public static Gen<T> Where<T>(this Gen<T> gen, Func<T, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var t = gen.Generate(pcg, null, out size);
            if (predicate(t)) return t;
        }
    });

    public static Gen<(T1, T2)> Where<T1, T2>(this Gen<(T1, T2)> gen, Func<T1, T2, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2)) return (t1, t2);
        }
    });

    public static Gen<(T1, T2, T3)> Where<T1, T2, T3>(this Gen<(T1, T2, T3)> gen, Func<T1, T2, T3, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3)) return (t1, t2, t3);
        }
    });

    public static Gen<(T1, T2, T3, T4)> Where<T1, T2, T3, T4>(this Gen<(T1, T2, T3, T4)> gen, Func<T1, T2, T3, T4, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3, t4) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3, t4)) return (t1, t2, t3, t4);
        }
    });

    public static Gen<(T1, T2, T3, T4, T5)> Where<T1, T2, T3, T4, T5>(this Gen<(T1, T2, T3, T4, T5)> gen, Func<T1, T2, T3, T4, T5, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3, t4, t5) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3, t4, t5)) return (t1, t2, t3, t4, t5);
        }
    });

    public static Gen<(T1, T2, T3, T4, T5, T6)> Where<T1, T2, T3, T4, T5, T6>(this Gen<(T1, T2, T3, T4, T5, T6)> gen, Func<T1, T2, T3, T4, T5, T6, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3, t4, t5, t6) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3, t4, t5, t6)) return (t1, t2, t3, t4, t5, t6);
        }
    });

    public static Gen<(T1, T2, T3, T4, T5, T6, T7)> Where<T1, T2, T3, T4, T5, T6, T7>(this Gen<(T1, T2, T3, T4, T5, T6, T7)> gen, Func<T1, T2, T3, T4, T5, T6, T7, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3, t4, t5, t6, t7) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3, t4, t5, t6, t7)) return (t1, t2, t3, t4, t5, t6, t7);
        }
    });

    public static Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> Where<T1, T2, T3, T4, T5, T6, T7, T8>(this Gen<(T1, T2, T3, T4, T5, T6, T7, T8)> gen, Func<T1, T2, T3, T4, T5, T6, T7, T8, bool> predicate) => Create((PCG pcg, Size min, out Size size) =>
    {
        while (true)
        {
            var (t1, t2, t3, t4, t5, t6, t7, t8) = gen.Generate(pcg, null, out size);
            if (predicate(t1, t2, t3, t4, t5, t6, t7, t8)) return (t1, t2, t3, t4, t5, t6, t7, t8);
        }
    });

    public static Gen<T> Const<T>(T value) => Create((PCG pcg, Size min, out Size size) => { size = new Size(0L); return value; });

    public static Gen<T> Const<T>(Func<T> value) => Create((PCG pcg, Size min, out Size size) => { size = new Size(0L); return value(); });

    public static Gen<T> OneOfConst<T>(params T[] ts) => Create((PCG pcg, Size min, out Size size) =>
    {
        size = new Size(0x1_0000_0000UL);
        if (min is not null && min.I < size.I) return default;
        return ts[pcg.Next((uint)ts.Length)];
    });

    public static Gen<T> OneOf<T>(params IGen<T>[] gens) => Create((PCG pcg, Size min, out Size size) =>
    {
        if (min is not null)
        {
            if (min.I < 0x1_0000_0000UL) { size = new Size(0x1_0000_0000UL); return default; }
            min = min.I == 0x1_0000_0000UL ? min.Next : null;
        }
        var r = gens[pcg.Next((uint)gens.Length)].Generate(pcg, min, out size);
        size = new Size(0x1_0000_0000UL, size);
        return r;
    });

    public static Gen<T> Enum<T>() where T : Enum
    {
        var a = System.Enum.GetValues(typeof(T));
        var ts = new T[a.Length];
        for (int i = 0; i < ts.Length; i++)
            ts[i] = (T)a.GetValue(i);
        return OneOfConst(ts);
    }

    public static Gen<T> FrequencyConst<T>(params (int, T)[] ts)
    {
        int total = 0;
        foreach (var (i, _) in ts) total += i;
        return Create((PCG pcg, Size min, out Size size) =>
        {
            size = new Size(0x1_0000_0000UL);
            if (min is not null && min.I < size.I) return default;
            var v = (int)pcg.Next((uint)total);
            foreach (var i in ts)
            {
                v -= i.Item1;
                if (v < 0) return i.Item2;
            }
            return default;
        });
    }

    public static Gen<T> Frequency<T>(params (int, IGen<T>)[] gens)
    {
        int total = 0;
        foreach (var (i, _) in gens) total += i;
        return Create((PCG pcg, Size min, out Size size) =>
        {
            if (min is not null)
            {
                if (min.I < 0x1_0000_0000UL) { size = new Size(0x1_0000_0000UL); return default; }
                min = min.I == 0x1_0000_0000UL ? min.Next : null;
            }
            var v = (int)pcg.Next((uint)total);
            foreach (var i in gens)
            {
                v -= i.Item1;
                if (v < 0)
                {
                    var r = i.Item2.Generate(pcg, min, out size);
                    size = new Size(0x1_0000_0000UL, size);
                    return r;
                }
            }
            size = new Size(0L);
            return default;
        });
    }

    public static Gen<T> Recursive<T>(Func<Gen<T>, Gen<T>> f)
    {
        Gen<T> gen = null;
        gen = f(Create((PCG pcg, Size min, out Size size) => gen.Generate(pcg, null, out size)));
        return gen;
    }

    public static Gen<T> Recursive<T>(Func<Gen<T>, Gen<T>> f, GenMap<T> map)
    {
        Gen<T> gen = null;
        gen = f(Create((PCG pcg, Size min, out Size size) => map(gen.Generate(pcg, null, out size), ref size)));
        return gen;
    }

    /// <summary>Create a second exact clone by running the generator again with the same seed.</summary>
    public static Gen<(T V0, T V1)> Clone<T>(this Gen<T> gen) => Create((PCG pcg, Size min, out Size size) =>
    {
        var seed = pcg.Seed;
        var stream = pcg.Stream;
        var r1 = gen.Generate(pcg, min, out size);
        var r2 = gen.Generate(new PCG(stream, seed), min, out _);
        return (r1, r2);
    });

    public static GenDictionary<K, V> Dictionary<K, V>(this Gen<K> genK, Gen<V> genV) => new(genK, genV);

    public static GenSortedDictionary<K, V> SortedDictionary<K, V>(this Gen<K> genK, Gen<V> genV) => new(genK, genV);

    static void Shuffle<T>(IList<T> a, PCG pcg, int lower)
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

    public static Gen<T[]> Shuffle<T>(T[] a) => Create((PCG pcg, Size min, out Size size) =>
    {
        Shuffle(a, pcg, 0);
        size = new Size(0L);
        return a;
    });

    public static Gen<T[]> Shuffle<T>(this Gen<T[]> gen) => Create((PCG pcg, Size min, out Size size) =>
    {
        var a = gen.Generate(pcg, null, out size);
        Shuffle(a, pcg, 0);
        return a;
    });

    public static Gen<T[]> Shuffle<T>(T[] a, int length) => Create((PCG pcg, Size min, out Size size) =>
    {
        size = new Size(0L);
        int lower = Math.Max(a.Length - length, 0);
        Shuffle(a, pcg, lower);
        if (lower == 0) return a;
        var r = new T[length];
        for (int i = 0; i < r.Length; i++)
            r[i] = a[i + lower];
        return r;
    });

    public static Gen<T[]> Shuffle<T>(T[] a, int start, int finish) =>
        Int[start, finish].SelectMany(i => Shuffle(a, i));

    public static Gen<T[]> Shuffle<T>(this Gen<T[]> gen, int length) =>
        SelectMany(gen, a => Shuffle(a, length));

    public static Gen<T[]> Shuffle<T>(this Gen<T[]> gen, int start, int finish) =>
        SelectMany(gen, Int[start, finish], (a, l) => Shuffle(a, l));

    public static Gen<List<T>> Shuffle<T>(List<T> a) => Create((PCG pcg, Size min, out Size size) =>
    {
        Shuffle(a, pcg, 0);
        size = new Size(0L);
        return a;
    });

    public static Gen<List<T>> Shuffle<T>(this Gen<List<T>> gen) => Create((PCG pcg, Size min, out Size size) =>
    {
        var a = gen.Generate(pcg, null, out size);
        Shuffle(a, pcg, 0);
        return a;
    });

    public static Gen<List<T>> Shuffle<T>(List<T> a, int length) => Create((PCG pcg, Size min, out Size size) =>
    {
        size = new Size(0L);
        int lower = Math.Max(a.Count - length, 0);
        Shuffle(a, pcg, lower);
        if (lower == 0) return a;
        var r = new List<T>(length);
        for (int i = 0; i < length; i++)
            r.Add(a[i + lower]);
        return r;
    });

    public static Gen<List<T>> Shuffle<T>(List<T> a, int start, int finish) =>
        SelectMany(Int[start, finish], l => Shuffle(a, l));

    public static Gen<List<T>> Shuffle<T>(this Gen<List<T>> gen, int length) =>
        SelectMany(gen, a => Shuffle(a, length));

    public static Gen<List<T>> Shuffle<T>(this Gen<List<T>> gen, int start, int finish) =>
        SelectMany(gen, Int[start, finish], (a, l) => Shuffle(a, l));

    public static GenOperation<T> Operation<T>(string name, Action<T> action)
        => new((PCG pcg, Size min, out Size size) => { size = new Size(0L); return (name, action); });

    public static GenOperation<T> Operation<T>(Action<T> action)
        => new((PCG pcg, Size min, out Size size) => { size = new Size(0L); return ("", action); }, true);

    public static GenOperation<Actual, Model> Operation<Actual, Model>(string name, Action<Actual, Model> action)
        => new((PCG pcg, Size min, out Size size) => { size = new Size(0L); return (name, action); });

    public static GenOperation<Actual, Model> Operation<Actual, Model>(Action<Actual, Model> action)
        => new((PCG pcg, Size min, out Size size) => { size = new Size(0L); return ("", action); }, true);

    public static readonly GenBool Bool = new();
    public static readonly GenSByte SByte = new();
    public static readonly GenByte Byte = new();
    public static readonly GenShort Short = new();
    public static readonly GenUShort UShort = new();
    public static readonly GenInt Int = new();
    public static readonly GenUInt UInt = new();
    public static readonly GenLong Long = new();
    public static readonly GenULong ULong = new();
    public static readonly GenFloat Float = new();
    public static readonly GenFloat Single = Float;
    public static readonly GenDouble Double = new();
    public static readonly GenDecimal Decimal = new();
    public static readonly GenDate Date = new();
    public static readonly GenDateTime DateTime = new();
    public static readonly GenTimeSpan TimeSpan = new();
    public static readonly GenDateTimeOffset DateTimeOffset = new();
    public static readonly GenGuid Guid = new();
    public static readonly GenChar Char = new();
    public static readonly GenString String = new();
}

public class GenBool : Gen<bool>
{
    public override bool Generate(PCG pcg, Size min, out Size size)
    {
        uint i = pcg.Next();
        size = new Size(i + 1UL);
        return (i & 1U) == 0U;
    }
}

public class GenSByte : Gen<sbyte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong Zigzag(sbyte i) => (ulong)((i << 1) ^ (i >> 7));
    public override sbyte Generate(PCG pcg, Size min, out Size size)
    {
        sbyte i = (sbyte)(pcg.Next() & 255u);
        size = new Size(Zigzag(i) + 1UL);
        return i;
    }
    public Gen<sbyte> this[sbyte start, sbyte finish]
    {
        get
        {
            uint l = (uint)(finish - start) + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                sbyte i = (sbyte)(start + pcg.Next(l));
                size = new Size(Zigzag(i) + 1UL);
                return i;
            });
        }
    }
}

public class GenByte : Gen<byte>
{
    public override byte Generate(PCG pcg, Size min, out Size size)
    {
        byte i = (byte)(pcg.Next() & 255u);
        size = new Size(i + 1UL);
        return i;
    }
    public Gen<byte> this[byte start, byte finish]
    {
        get
        {
            uint s = start;
            uint l = finish - s + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                byte i = (byte)(s + pcg.Next(l));
                size = new Size(i + 1UL);
                return i;
            });
        }
    }
}

public class GenShort : Gen<short>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Zigzag(short i) => (ushort)(i << 1 ^ i >> 31);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short Unzigzag(ushort i) => (short)((i >> 1) ^ -(int)(i & 1U));
    public override short Generate(PCG pcg, Size min, out Size size)
    {
        uint s = pcg.Next() & 15U;
        ushort i = (ushort)(1U << (int)s);
        i = (ushort)((pcg.Next() & (i - 1) | i) - 1);
        size = new Size((s << 11 | i & 0x7FFUL) + 1UL);
        return (short)-Unzigzag(i);
    }
    public Gen<short> this[short start, short finish]
    {
        get
        {
            uint l = (uint)(finish - start) + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                short i = (short)(start + pcg.Next(l));
                size = new Size(Zigzag(i) + 1UL);
                return i;
            });
        }
    }
}

public class GenUShort : Gen<ushort>
{
    public override ushort Generate(PCG pcg, Size min, out Size size)
    {
        ushort i = (ushort)(pcg.Next() & 65535u);
        size = new Size(i + 1UL);
        return i;
    }
    public Gen<ushort> this[ushort start, ushort finish]
    {
        get
        {
            uint l = (uint)(finish - start) + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                ushort i = (ushort)(start + pcg.Next(l));
                size = new Size(i + 1UL);
                return i;
            });
        }
    }
}

public class GenInt : Gen<int>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Zigzag(int i) => (uint)(i << 1 ^ i >> 31);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Unzigzag(uint i) => (int)(i >> 1) ^ -(int)(i & 1U);
    public override int Generate(PCG pcg, Size min, out Size size)
    {
        uint s = pcg.Next() & 31U;
        uint i = 1U << (int)s;
        i = (pcg.Next() & (i - 1) | i) - 1;
        size = new Size((s << 27 | i & 0x7FFFFFFUL) + 1UL);
        return -Unzigzag(i);
    }
    public Gen<int> Uniform = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        int i = (int)pcg.Next();
        size = new Size(Zigzag(i) + 1UL);
        return i;
    });
    public Gen<int> Positive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint s = pcg.Next(31);
        int i = 1 << (int)s;
        i = (int)pcg.Next() & (i - 1) | i;
        size = new Size((s << 27 | (ulong)i & 0x7FFFFFFUL) + 1UL);
        return i;
    });
    public Gen<int> NonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint s = pcg.Next(31);
        int i = 1 << (int)s;
        i = ((int)pcg.Next() & (i - 1) | i) - 1;
        size = new Size((s << 27 | (ulong)i & 0x7FFFFFFUL) + 1UL);
        return i;
    });
    public Gen<int> this[int start, int finish]
    {
        get
        {
            uint l = (uint)(finish - start) + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                int i = (int)(start + pcg.Next(l));
                size = new Size(Zigzag(i) + 1UL);
                return i;
            });
        }
    }
    public class IntSkew
    {
        public Gen<int> this[int start, int finish, double a] =>
            a >= 0.0 ? Gen.Double.Unit.Select(u => start + (int)(Math.Pow(u, a + 1.0) * (1.0 + finish - start)))
            : Gen.Double.Unit.Select(u => finish - (int)(Math.Pow(u, 1.0 - a) * (1.0 + finish - start)));
    }
    /// <summary>Skew the distribution towards either end.
    /// For a&gt;0 (positive skewness) the median decreases to 0.5*Math.Pow(0.5,a), and the mean decreases to 1.0/(1.0+a) of the range.
    /// For a&lt;0 (negative skewness) the median increases to 1.0-0.5*Math.Pow(0.5,-a), and the mean increases 1.0-1.0/(1.0-a) of the range.</summary>
    public IntSkew Skew = new();
}

public class GenUInt : Gen<uint>
{
    public override uint Generate(PCG pcg, Size min, out Size size)
    {
        uint s = pcg.Next() & 31U;
        uint i = 1U << (int)s;
        i = (pcg.Next() & (i - 1) | i) - 1;
        size = new Size((s << 27 | i & 0x7FFF_FFFUL) + 1UL);
        return i;
    }
    public Gen<uint> this[uint start, uint finish]
    {
        get
        {
            uint l = finish - start + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                uint i = start + pcg.Next(l);
                size = new Size(i + 1UL);
                return i;
            });
        }
    }
    public class UIntSkew
    {
        public Gen<uint> this[double a] => Gen.Double.Unit.Select(u =>
        {
            var ua = Math.Pow(u, a + 1.0);
            return (uint)(ua * uint.MaxValue + ua);
        });
        public Gen<uint> this[uint start, uint finish, double a] =>
            a >= 0.0 ? Gen.Double.Unit.Select(u => start + (uint)(Math.Pow(u, a + 1.0) * (1.0 + finish - start)))
            : Gen.Double.Unit.Select(u => finish - (uint)(Math.Pow(u, 1.0 - a) * (1.0 + finish - start)));
    }
    /// <summary>Skew the distribution towards either end.
    /// For a&gt;0 (positive skewness) the median decreases to 0.5*Math.Pow(0.5,a), and the mean decreases to 1.0/(1.0+a) of the range.
    /// For a&lt;0 (negative skewness) the median increases to 1.0-0.5*Math.Pow(0.5,-a), and the mean increases 1.0-1.0/(1.0-a) of the range.</summary>
    public UIntSkew Skew = new();
}

public class GenLong : Gen<long>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Zigzag(long i) => (ulong)(i << 1 ^ i >> 63);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Unzigzag(ulong i) => (long)(i >> 1) ^ -(long)(i & 1UL);
    public override long Generate(PCG pcg, Size min, out Size size)
    {
        uint s = pcg.Next() & 63U;
        ulong i = 1UL << (int)s;
        i = (pcg.Next64() & (i - 1UL) | i) - 1UL;
        size = new Size(((ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU) + 1UL);
        return -Unzigzag(i);
    }
    public Gen<long> this[long start, long finish]
    {
        get
        {

            ulong l = (ulong)(finish - start) + 1ul;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                long i = start + (long)pcg.Next64(l);
                size = new Size(Zigzag(i) + 1UL);
                return i;
            });
        }
    }
}

public class GenULong : Gen<ulong>
{
    public override ulong Generate(PCG pcg, Size min, out Size size)
    {
        uint s = pcg.Next() & 63U;
        ulong i = 1UL << (int)s;
        i = (pcg.Next64() & (i - 1UL) | i) - 1UL;
        size = new Size(((ulong)s << 46 | i & 0x3FFF_FFFF_FFFFU) + 1UL);
        return i;
    }
    public Gen<ulong> this[ulong start, ulong finish]
    {
        get
        {
            ulong l = finish - start + 1ul;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                ulong i = start + pcg.Next64(l);
                size = new Size(i + 1UL);
                return i;
            });
        }
    }
}

public class GenFloat : Gen<float>
{
    [StructLayout(LayoutKind.Explicit)]
    struct FloatConverter
    {
        [FieldOffset(0)] public uint I;
        [FieldOffset(0)] public float F;
    }
    public override float Generate(PCG pcg, Size min, out Size size)
    {
        uint i = pcg.Next();
        size = new Size(i + 1UL);
        return new FloatConverter { I = i }.F;
    }
    public Gen<float> this[float start, float finish]
    {
        get
        {
            finish -= start;
            start -= finish;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                uint i = pcg.Next() >> 9;
                size = new Size(i + 1UL);
                return new FloatConverter { I = i | 0x3F800000 }.F * finish + start;
            });
        }
    }
    /// <summary>In the range 0.0 &lt;= x &lt;= max including special values.</summary>
    public Gen<float> NonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next();
        size = new Size(i + 1UL);
        return Math.Abs(new FloatConverter { I = i }.F);
    });
    /// <summary>In the range 0.0f &lt;= x &lt; 1.0f.</summary>
    public Gen<float> Unit = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 9;
        size = new Size(i + 1UL);
        return new FloatConverter { I = i | 0x3F800000 }.F - 1f;
    });
    /// <summary>In the range 1.0f &lt;= x &lt; 2.0f.</summary>
    public Gen<float> OneTwo = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 9;
        size = new Size(i + 1UL);
        return new FloatConverter { I = i | 0x3F800000 }.F;
    });
    /// <summary>In the range 0.0 &lt; x &lt;= inf without nan.</summary>
    public Gen<float> Positive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U ? (i & 0xFU) + 1U : new FloatConverter { I = i + 1U }.F;
    });
    /// <summary>In the range -inf &lt;= x &lt; 0.0 without nan.</summary>
    public Gen<float> Negative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U ? -((i & 0xFU) + 1U) : new FloatConverter { I = (i + 1U) | 0x80000000U }.F;
    });
    /// <summary>Without special values nan and inf.</summary>
    public Gen<float> Normal = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next();
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U ? (8f - (i & 0xFU)) : new FloatConverter { I = i }.F;
    });
    /// <summary>In the range 0.0 &lt; x &lt;= max without special values nan and inf.</summary>
    public Gen<float> NormalPositive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U || i == 0U ? (i & 0xFU) + 1U : new FloatConverter { I = i }.F;
    });
    /// <summary>In the range min &lt;= x &lt; 0.0 without special values nan and inf.</summary>
    public Gen<float> NormalNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U || i == 0U ? -((i & 0xFU) + 1U) : new FloatConverter { I = i | 0x80000000U }.F;
    });
    /// <summary>In the range 0.0 &lt;= x &lt;= max without special values nan and inf.</summary>
    public Gen<float> NormalNonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U ? i & 0xFU : new FloatConverter { I = i }.F;
    });
    /// <summary>In the range min &lt;= x &lt;= 0.0 without special values nan and inf.</summary>
    public Gen<float> NormalNonPositive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next() >> 1;
        size = new Size(i + 1UL);
        return (i & 0x7F800000U) == 0x7F800000U ? -(i & 0xFU) : new FloatConverter { I = i | 0x80000000U }.F;
    });
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

    /// <summary>With more special values like nan, inf, max, epsilon, -2, -1, 0, 1, 2.</summary>
    public Gen<float> Special = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        uint i = pcg.Next();
        size = new Size(i + 1UL);
        return (i & 0xF0U) == 0xD0U ? MakeSpecial(i) : new FloatConverter { I = i }.F;
    });
}

public class GenDouble : Gen<double>
{
    public override double Generate(PCG pcg, Size min, out Size size)
    {
        ulong i = pcg.Next64();
        size = new Size((i >> 12) + 1UL);
        return BitConverter.Int64BitsToDouble((long)i);
    }
    public Gen<double> this[double start, double finish]
    {
        get
        {
            finish -= start;
            start -= finish;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                ulong i = pcg.Next64() >> 12;
                size = new Size(i + 1UL);
                return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * finish + start;
            });
        }
    }
    /// <summary>In the range 0.0 &lt;= x &lt;= inf, can be nan.</summary>
    public Gen<double> NonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64();
        size = new Size((i >> 12) + 1UL);
        return Math.Abs(BitConverter.Int64BitsToDouble((long)i));
    });
    /// <summary>In the range 0.0 &lt;= x &lt; 1.0.</summary>
    public Gen<double> Unit = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 12;
        size = new Size(i + 1UL);
        return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1.0;
    });
    /// <summary>In the range 1.0 &lt;= x &lt; 2.0.</summary>
    public Gen<double> OneTwo = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 12;
        size = new Size(i + 1UL);
        return BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000);
    });
    /// <summary>In the range 0.0 &lt; x &lt;= inf without nan.</summary>
    public Gen<double> Positive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? ((i & 0xFUL) + 1UL) : BitConverter.Int64BitsToDouble((long)(i + 1UL));
    });
    /// <summary>In the range -inf &lt;= x &lt; 0.0 without nan.</summary>
    public Gen<double> Negative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? -(double)((i & 0xFUL) + 1UL) : BitConverter.Int64BitsToDouble((long)((i + 1UL) | 0x8000000000000000U));
    });
    /// <summary>Without special values nan and inf.</summary>
    public Gen<double> Normal = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64();
        size = new Size((i >> 12) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? (8.0 - (i & 0xFUL)) : BitConverter.Int64BitsToDouble((long)i);
    });
    /// <summary>In the range 0.0 &lt; x &lt;= max without special values nan and inf.</summary>
    public Gen<double> NormalPositive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U || i == 0L ? ((i & 0xFUL) + 1UL) : BitConverter.Int64BitsToDouble((long)i);
    });
    /// <summary>In the range min &lt;= x &lt; 0.0 without special values nan and inf.</summary>
    public Gen<double> NormalNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U || i == 0L ? -(double)((i & 0xFUL) + 1UL) : BitConverter.Int64BitsToDouble((long)(i | 0x8000000000000000U));
    });
    /// <summary>In the range 0.0 &lt;= x &lt;= max without special values nan and inf.</summary>
    public Gen<double> NormalNonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? i & 0xFUL : BitConverter.Int64BitsToDouble((long)i);
    });
    /// <summary>In the range min &lt;= x &lt;= 0.0 without special values nan and inf.</summary>
    public Gen<double> NormalNonPositive = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 1;
        size = new Size((i >> 11) + 1UL);
        return (i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? -(double)(i & 0xFUL) : BitConverter.Int64BitsToDouble((long)(i | 0x8000000000000000U));
    });
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
    /// <summary>With more special values like nan, inf, max, epsilon, -2, -1, 0, 1, 2.</summary>
    public Gen<double> Special = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64();
        size = new Size((i >> 12) + 1UL);
        return (i & 0xF0UL) == 0xD0UL ? MakeSpecial(i) : BitConverter.Int64BitsToDouble((long)i);
    });
    public class DoubleSkew
    {
        public Gen<double> this[double start, double finish, double a] =>
            a >= 0.0 ? Gen.Double.Unit.Select(u => start + Math.Pow(u, a + 1.0) * (finish - start))
            : Gen.Double.Unit.Select(u => finish - Math.Pow(u, 1.0 - a) * (finish - start));
    }
    /// <summary>Skew the distribution towards either end.
    /// For a&gt;0 (positive skewness) the median decreases to 0.5*Math.Pow(0.5,a), and the mean decreases to 1.0/(1.0+a) of the range.
    /// For a&lt;0 (negative skewness) the median increases to 1.0-0.5*Math.Pow(0.5,-a), and the mean increases 1.0-1.0/(1.0-a) of the range.</summary>
    public DoubleSkew Skew = new();
}

public class GenDecimal : Gen<decimal>
{
    public override decimal Generate(PCG pcg, Size min, out Size size)
    {
        var scaleAndSign = (int)pcg.Next(58);
        var hi = pcg.Next();
        size = new Size(((ulong)scaleAndSign << 32) + hi + 1UL);
        return new decimal((int)pcg.Next(), (int)pcg.Next(), (int)hi, (scaleAndSign & 1) == 1, (byte)(scaleAndSign >> 1));
    }
    public Gen<decimal> this[decimal start, decimal finish]
    {
        get
        {
            finish -= start;
            start -= finish;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var i = pcg.Next64() >> 12;
                size = new Size(i + 1UL);
                return (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * finish + start;
            });
        }
    }
    public Gen<decimal> NonNegative = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        var scale = (byte)pcg.Next(29);
        var hi = (int)pcg.Next();
        size = new Size((ulong)scale << 32 + hi);
        return new decimal((int)pcg.Next(), (int)pcg.Next(), hi, false, scale);
    });
    public Gen<decimal> Unit = Gen.Create((PCG pcg, Size min, out Size size) =>
    {
        ulong i = pcg.Next64() >> 12;
        size = new Size(i + 1UL);
        return (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1M;
    });
}

public class GenDateTime : Gen<DateTime>
{
    const ulong max = 3155378975999999999UL; //(ulong)DateTime.MaxValue.Ticks;
    public override DateTime Generate(PCG pcg, Size min, out Size size)
    {
        ulong i = pcg.Next64(max);
        size = new Size((i >> 10) + 1UL);
        return new DateTime((long)i);
    }
    public Gen<DateTime> this[DateTime start, DateTime finish]
    {
        get
        {
            ulong l = (ulong)(finish.Ticks - start.Ticks) + 1ul;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                ulong i = (ulong)start.Ticks + pcg.Next64(l);
                size = new Size(i + 1UL);
                return new DateTime((long)i);
            });
        }
    }
}

public class GenDate : Gen<DateTime>
{
    const uint max = 3652058U; //(uint)(DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay);
    public override DateTime Generate(PCG pcg, Size min, out Size size)
    {
        uint i = pcg.Next(max);
        size = new Size(i + 1UL);
        return new DateTime(i * TimeSpan.TicksPerDay);
    }
    public Gen<DateTime> this[DateTime start, DateTime finish]
    {
        get
        {
            uint s = (uint)(start.Ticks / TimeSpan.TicksPerDay);
            uint l = (uint)((finish.Ticks - start.Ticks) / TimeSpan.TicksPerDay) + 1u;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                uint i = s + pcg.Next(l);
                size = new Size(i + 1UL);
                return new DateTime(i * TimeSpan.TicksPerDay);
            });
        }
    }
}

public class GenTimeSpan : Gen<TimeSpan>
{
    public override TimeSpan Generate(PCG pcg, Size min, out Size size)
    {
        ulong i = pcg.Next64();
        size = new Size((i >> 12) + 1UL);
        return new TimeSpan((long)i);
    }
    public Gen<TimeSpan> this[TimeSpan start, TimeSpan finish]
    {
        get
        {
            ulong l = (ulong)(finish.Ticks - start.Ticks) + 1ul;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                ulong i = (ulong)start.Ticks + pcg.Next64(l);
                size = new Size(i + 1UL);
                return new TimeSpan((long)i);
            });
        }
    }
}

public class GenDateTimeOffset : Gen<DateTimeOffset>
{
    readonly Gen<DateTime> genDateTime = Gen.DateTime[new DateTime(1800, 1, 1), new DateTime(2200, 1, 1)];
    readonly Gen<int> genOffset = Gen.Int[-14 * 60, 14 * 60];
    public override DateTimeOffset Generate(PCG pcg, Size min, out Size size)
    {
        var os = genOffset.Generate(pcg, null, out Size s1);
        var dt = genDateTime.Generate(pcg, null, out Size s2);
        size = new Size(s1.I, s2);
        return new DateTimeOffset(dt, TimeSpan.FromMinutes(os));
    }
}

public class GenGuid : Gen<Guid>
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
    public override Guid Generate(PCG pcg, Size min, out Size size)
    {
        var c = new GuidConverter { I0 = pcg.Next(), I1 = pcg.Next(), I2 = pcg.Next(), I3 = pcg.Next() };
        size = new Size(c.I0 + c.I1 + c.I2 + c.I3 + 1UL);
        return c.G;
    }
}

public class GenChar : Gen<char>
{
    public override char Generate(PCG pcg, Size min, out Size size)
    {
        var i = pcg.Next() & 127u;
        size = new Size(i + 1UL);
        return (char)i;
    }
    public Gen<char> this[char start, char finish]
    {
        get
        {
            uint s = start;
            uint l = finish + 1u - s;
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var i = pcg.Next(l);
                size = new Size(i + 1UL);
                return (char)(s + i);
            });
        }
    }
    public Gen<char> this[string chars]
    {
        get
        {
            return Gen.Create((PCG pcg, Size min, out Size size) =>
            {
                var i = pcg.Next((uint)chars.Length);
                size = new Size(i + 1UL);
                return chars[(int)i];
            });
        }
    }
}

public class GenString : Gen<string>
{
    static readonly Gen<string> d = Gen.Char.Array.Select(i => new string(i));
    public override string Generate(PCG pcg, Size min, out Size size) => d.Generate(pcg, min, out size);
    public Gen<string> this[int start, int finish] =>
        Gen.Char.Array[start, finish].Select(i => new string(i));
    public Gen<string> this[Gen<char> gen, int start, int finish] =>
        gen.Array[start, finish].Select(i => new string(i));
    public Gen<string> this[Gen<char> gen] =>
        gen.Array.Select(i => new string(i));
    public Gen<string> this[string chars] =>
        Gen.Char[chars].Array.Select(i => new string(i));
}

public class GenArray<T> : Gen<T[]>
{
    readonly Gen<T> gen;
    public GenArray(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T[] Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new T[length];
        if (min is not null)
        {
            if (min.I < sizeI) { size = new Size(sizeI); return vs; }
            min = min.I == sizeI ? min.Next : null;
        }
        var total = new Size(0L);
        for (int i = 0; i < vs.Length; i++)
        {
            vs[i] = gen.Generate(pcg, min, out var si);
            total.Add(si);
        }
        size = new Size(sizeI, total);
        return vs;
    }
    public override T[] Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<T[]> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out Size sl), out size)
    );
    public Gen<T[]> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<T[]> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<T[]> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenArrayUnique<T> : Gen<T[]>
{
    readonly Gen<T> gen;
    public GenArrayUnique(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T[] Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new T[length];
        if (min is not null && min.I < sizeI) { size = new Size(sizeI); return vs; }
        size = new Size(0L);
        var hs = new HashSet<T>();
        int i = 0;
        while (i < length)
        {

            var v = gen.Generate(pcg, null, out var s);
            var bad = 0;
            if (hs.Add(v))
            {
                vs[i++] = v;
                size.Add(s);
            }
            else if (++bad == 1000) throw new CsCheckException("Failing to add to ArrayUnique");
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override T[] Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<T[]> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, min, out size), out size)
    );
    public Gen<T[]> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<T[]> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<T[]> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenEnumerable<T> : Gen<IEnumerable<T>>
{
    readonly Gen<T> gen;
    public GenEnumerable(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerable<T> Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new T[length];
        if (min is not null)
        {
            if (min.I < sizeI) { size = new Size(sizeI); return vs; }
            min = min.I == sizeI ? min.Next : null;
        }
        size = new Size(0L);
        for (int i = 0; i < vs.Length; i++)
        {
            vs[i] = gen.Generate(pcg, min, out var s);
            size.Add(s);
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override IEnumerable<T> Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<IEnumerable<T>> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out _), out size)
    );
    public Gen<IEnumerable<T>> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<IEnumerable<T>> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<IEnumerable<T>> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenArray2D<T> : Gen<T[,]>
{
    readonly Gen<T> gen;
    public GenArray2D(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T[,] Generate(PCG pcg, Size min, int length0, int length1, out Size size)
    {
        size = new Size((((ulong)(length0 * length1)) << 32) + 1UL);
        var vs = new T[length0, length1];
        if (min is not null && min.I < size.I) return vs;
        for (int i = 0; i < length0; i++)
            for (int j = 0; j < length1; j++)
                vs[i, j] = gen.Generate(pcg, null, out _);
        return vs;
    }
    public override T[,] Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), (int)(pcg.Next() & 127U), out size);
    public Gen<T[,]> this[int length0, int length1] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length0, length1, out size)
    );
    public Gen<T[,]> this[Gen<int> length0, Gen<int> length1] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length0.Generate(pcg, null, out _), length1.Generate(pcg, null, out _), out size)
    );
    public Gen<T[,]> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenList<T> : Gen<List<T>>
{
    readonly Gen<T> gen;
    public GenList(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    List<T> Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new List<T>(length);
        if (min is not null)
        {
            if (min.I < sizeI) { size = new Size(sizeI); return vs; }
            min = min.I == sizeI ? min.Next : null;
        }
        size = new Size(0L);
        for (int i = 0; i < length; i++)
        {
            vs.Add(gen.Generate(pcg, min, out var s));
            size.Add(s);
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override List<T> Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<List<T>> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out _), out size)
    );
    public Gen<List<T>> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<List<T>> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<List<T>> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenHashSet<T> : Gen<HashSet<T>>
{
    readonly Gen<T> gen;
    public GenHashSet(Gen<T> gen) => this.gen = gen;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    HashSet<T> Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new HashSet<T>();
        if (min is not null && min.I < sizeI) { size = new Size(sizeI); return vs; }
        size = new Size(0L);
        var bad = 0;
        while (length > 0)
        {
            if (vs.Add(gen.Generate(pcg, null, out var s)))
            {
                size.Add(s);
                length--;
                bad = 0;
            }
            else if (++bad == 1000) throw new CsCheckException("Failing to add to HashSet");
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override HashSet<T> Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<HashSet<T>> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out _), out size)
    );
    public Gen<HashSet<T>> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<HashSet<T>> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<HashSet<T>> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenDictionary<K, V> : Gen<Dictionary<K, V>>
{
    readonly Gen<K> genK;
    readonly Gen<V> genV;
    public GenDictionary(Gen<K> genK, Gen<V> genV)
    {
        this.genK = genK;
        this.genV = genV;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Dictionary<K, V> Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new Dictionary<K, V>(length);
        if (min is not null && min.I < sizeI) { size = new Size(sizeI); return vs; }
        size = new Size(0L);
        var i = length;
        var bad = 0;
        while (i > 0)
        {
            var k = genK.Generate(pcg, null, out var s);
            if (!vs.ContainsKey(k))
            {
                size.Add(s);
                vs.Add(k, genV.Generate(pcg, null, out s));
                size.Add(s);
                i--;
                bad = 0;
            }
            else if (++bad == 1000) throw new CsCheckException("Failing to add to Dictionary");
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override Dictionary<K, V> Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<Dictionary<K, V>> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out _), out size)
    );
    public Gen<Dictionary<K, V>> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<Dictionary<K, V>> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<Dictionary<K, V>> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenSortedDictionary<K, V> : Gen<SortedDictionary<K, V>>
{
    readonly Gen<K> genK;
    readonly Gen<V> genV;
    public GenSortedDictionary(Gen<K> genK, Gen<V> genV)
    {
        this.genK = genK;
        this.genV = genV;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    SortedDictionary<K, V> Generate(PCG pcg, Size min, int length, out Size size)
    {
        var sizeI = ((ulong)length << 32) + 1UL;
        var vs = new SortedDictionary<K, V>();
        if (min is not null && min.I < sizeI) { size = new Size(sizeI); return vs; }
        size = new Size(0L);
        var i = length;
        var bad = 0;
        while (i > 0)
        {
            var k = genK.Generate(pcg, null, out var s);
            if (!vs.ContainsKey(k))
            {
                size.Add(s);
                vs.Add(k, genV.Generate(pcg, null, out s));
                size.Add(s);
                i--;
                bad = 0;
            }
            else if (++bad == 1000) throw new CsCheckException("Failing to add to SortedDictionary");
        }
        size = new Size(sizeI, size);
        return vs;
    }
    public override SortedDictionary<K, V> Generate(PCG pcg, Size min, out Size size)
        => Generate(pcg, min, (int)(pcg.Next() & 127U), out size);
    public Gen<SortedDictionary<K, V>> this[Gen<int> length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length.Generate(pcg, null, out _), out size)
    );
    public Gen<SortedDictionary<K, V>> this[int length] => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, length, out size)
    );
    public Gen<SortedDictionary<K, V>> this[int start, int finish] => this[Gen.Int[start, finish]];
    public Gen<SortedDictionary<K, V>> Nonempty => Gen.Create((PCG pcg, Size min, out Size size) =>
        Generate(pcg, min, (int)(pcg.Next() & 127U) + 1, out size)
    );
}

public class GenOperation<T> : Gen<(string, Action<T>)>
{
    public bool AddOpNumber;
    readonly GenDelegate<(string, Action<T>)> generate;
    internal GenOperation(GenDelegate<(string, Action<T>)> generate) => this.generate = generate;
    internal GenOperation(GenDelegate<(string, Action<T>)> generate, bool addOpNumber)
    {
        this.generate = generate;
        AddOpNumber = addOpNumber;
    }
    public override (string, Action<T>) Generate(PCG pcg, Size min, out Size size) => generate(pcg, min, out size);
}

public class GenOperation<T1, T2> : Gen<(string, Action<T1, T2>)>
{
    public bool AddOpNumber;
    readonly GenDelegate<(string, Action<T1, T2>)> generate;
    internal GenOperation(GenDelegate<(string, Action<T1, T2>)> generate) => this.generate = generate;
    internal GenOperation(GenDelegate<(string, Action<T1, T2>)> generate, bool addOpNumber)
    {
        this.generate = generate;
        AddOpNumber = addOpNumber;
    }
    public override (string, Action<T1, T2>) Generate(PCG pcg, Size min, out Size size) => generate(pcg, min, out size);
}

public class GenMetamorphic<T> : Gen<(string, Action<T>, Action<T>)>
{
    readonly GenDelegate<(string, Action<T>, Action<T>)> generate;
    internal GenMetamorphic(GenDelegate<(string, Action<T>, Action<T>)> generate) => this.generate = generate;

    public override (string, Action<T>, Action<T>) Generate(PCG pcg, Size min, out Size size) => generate(pcg, min, out size);
}