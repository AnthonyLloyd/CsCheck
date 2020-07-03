using System;
using System.Collections.Generic;

namespace CsCheck
{
    public class Size
    {
        public ulong I { get; }
        public Size[] Next { get; }
        public Size(ulong i, Size[] next)
        {
            I = i;
            Next = next;
        }
    }

    public struct Gen<T>
    {
        public int Start { get; }
        public int Length { get; }
        public readonly Func<int, int, PCG, (T, Size)> Generate;
        public Gen(int start, int length, Func<int, int, PCG, (T, Size)> generate)
        {
            Start = start;
            Length = length;
            Generate = generate;
        }
        public Gen<T> Slice(int start, int length)
        {
            return new Gen<T>(start, length, Generate);
        }
    }

    public static class Gen
    {
        public static Gen<R> Select<T, R>(this Gen<T> gen, Func<T, R> selector) => new Gen<R>(gen.Start, gen.Length, (start,length,pcg) =>
        {
            var (v, s) = gen.Generate(start,length,pcg);
            return (selector(v), s);
        });
        public static Gen<R> Select<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2,
            Func<T1, T2, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            return (selector(v1, v2), new Size(0UL, new[] { s1, s2 }));
        });
        public static Gen<R> Select<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
            Func<T1, T2, T3, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            return (selector(v1, v2, v3), new Size(0UL, new[] { s1, s2, s3 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Func<T1, T2, T3, T4, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            return (selector(v1, v2, v3, v4), new Size(0UL, new[] { s1, s2, s3, s4 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Func<T1, T2, T3, T4, T5, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            var (v5, s5) = gen5.Generate(gen5.Start, gen5.Length, pcg);
            return (selector(v1, v2, v3, v4, v5), new Size(0UL, new[] { s1, s2, s3, s4, s5 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            var (v5, s5) = gen5.Generate(gen5.Start, gen5.Length, pcg);
            var (v6, s6) = gen6.Generate(gen6.Start, gen6.Length, pcg);
            return (selector(v1, v2, v3, v4, v5, v6), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            var (v5, s5) = gen5.Generate(gen5.Start, gen5.Length, pcg);
            var (v6, s6) = gen6.Generate(gen6.Start, gen6.Length, pcg);
            var (v7, s7) = gen7.Generate(gen7.Start, gen7.Length, pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            var (v5, s5) = gen5.Generate(gen5.Start, gen5.Length, pcg);
            var (v6, s6) = gen6.Generate(gen6.Start, gen6.Length, pcg);
            var (v7, s7) = gen7.Generate(gen7.Start, gen7.Length, pcg);
            var (v8, s8) = gen8.Generate(gen8.Start, gen8.Length, pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7, v8), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7, s8 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, R> selector) => new Gen<R>(0, 0, (_,__,pcg) =>
        {
            var (v1, s1) = gen1.Generate(gen1.Start, gen1.Length, pcg);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            var (v3, s3) = gen3.Generate(gen3.Start, gen3.Length, pcg);
            var (v4, s4) = gen4.Generate(gen4.Start, gen4.Length, pcg);
            var (v5, s5) = gen5.Generate(gen5.Start, gen5.Length, pcg);
            var (v6, s6) = gen6.Generate(gen6.Start, gen6.Length, pcg);
            var (v7, s7) = gen7.Generate(gen7.Start, gen7.Length, pcg);
            var (v8, s8) = gen8.Generate(gen8.Start, gen8.Length, pcg);
            var (v9, s9) = gen9.Generate(gen9.Start, gen9.Length, pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7, v8, v9), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9 }));
        });
        public static Gen<R> SelectMany<T, R>(this Gen<T> gen, Func<T, Gen<R>> selector) => new Gen<R>(gen.Start, gen.Length, (start, length, pcg) =>
        {
            var (v1, s1) = gen.Generate(start, length, pcg);
            var gen2 = selector(v1);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            return (v2, new Size(s1.I, new[] { s2 }));
        });
        public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Func<T1, Gen<T2>> genSelector, Func<T1, T2, R> resultSelector)
            => new Gen<R>(gen1.Start, gen1.Length, (start, length, pcg) =>
        {
            var (v1, s1) = gen1.Generate(start, length, pcg);
            var gen2 = genSelector(v1);
            var (v2, s2) = gen2.Generate(gen2.Start, gen2.Length, pcg);
            return (resultSelector(v1, v2), new Size(s1.I, new[] { s2 }));
        });
        public static readonly Gen<byte> Byte = new Gen<byte>(0, 256, (start, length, pcg) =>
        {
            var i = start + pcg.Next(length);
            return ((byte)i, new Size((ulong)i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<short> Short = new Gen<short>(short.MinValue, short.MaxValue - short.MaxValue, (start, length, pcg) =>
        {
            var i = start + pcg.Next(length);
            return ((short)i, new Size((ulong)i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<int> Int = new Gen<int>(0, 0, (start, length, pcg) =>
        {
            var i = length == 0 ? (int)pcg.Next() : start + pcg.Next(length);
            return (i, new Size((ulong)i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<uint> UInt = new Gen<uint>(0, 0, (start, length, pcg) =>
        {
            var i = length == 0 ? pcg.Next() : (uint)start + (uint)pcg.Next(length);
            return (i, new Size(i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<long> Long = new Gen<long>(0, 0, (start, length, pcg) =>
        {
            var i = length == 0 ? (long)pcg.Next64() : (long)start + pcg.Next(length);
            return (i, new Size((ulong)i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<ulong> ULong = new Gen<ulong>(0, 0, (start, length, pcg) =>
        {
            var i = length == 0 ? pcg.Next64() : (ulong)start + (ulong)pcg.Next64(length);
            return (i, new Size(i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<double> Double = new Gen<double>(-100, 201, (start, length, pcg) =>
        {
            var i = pcg.Next64() >> 12;
            var f = BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L) - 1.0;
            return (start + f * length, new Size(i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<char> Char = new Gen<char>(0, 128, (start, length, pcg) =>
        {
            var i = start + pcg.Next(length);
            return ((char)i, new Size((ulong)i, System.Array.Empty<Size>()));
        });
        public static readonly Gen<string> String = Char.Array().Select(i => new string(i));
        static readonly Size zero = new Size(0UL, System.Array.Empty<Size>());
        public static Gen<T> Const<T>(T value) => new Gen<T>(0, 0, (_, __, ___) => (value, zero));
        public static Gen<T[]> Array<T>(this Gen<T> gen, Gen<int> genLength) => new Gen<T[]>(genLength.Start, genLength.Length, (start, length, pcg) =>
        {
            var (l, sl) = genLength.Generate(start, length, pcg);
            var vs = new T[l];
            var ss = new Size[l];
            for (int i = 0; i < vs.Length; i++)
            {
                var (v, s) = gen.Generate(gen.Start, gen.Length, pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(sl.I, ss));
        });
        public static Gen<T[]> Array<T>(this Gen<T> gen) => Array(gen, Int[0..100]);
        public static Gen<T[]> Array<T>(this Gen<T> gen, int length) => Array(gen, Int[length..1]);
        public static Gen<List<T>> List<T>(this Gen<T> gen, Gen<int> genLength) => new Gen<List<T>>(genLength.Start, genLength.Length, (start, length, pcg) =>
        {
            var (l, sl) = genLength.Generate(start, length, pcg);
            var vs = new List<T>(l);
            var ss = new Size[l];
            for (int i = 0; i < l; i++)
            {
                var (v, s) = gen.Generate(gen.Start, gen.Length, pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(sl.I, ss));
        });
        public static Gen<List<T>> List<T>(this Gen<T> gen) => List(gen, Int[0..100]);
        public static Gen<List<T>> List<T>(this Gen<T> gen, int length) => List(gen, Int[length..1]);
        public static Gen<T> OneOf<T>(List<Gen<T>> gens) => Int.Slice(0, gens.Count).SelectMany(i => gens[i]);
    }
}

// TODO:
// Next(0) Next(1) vs 1 should cause a Next()
// Enumerable ?