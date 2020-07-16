// Copyright 2020 Anthony Lloyd
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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        internal bool IsLessThan(Size s) => I == s.I ? IsLessThan(Next, s.Next) : I < s.I;
        static bool IsLessThan(IEnumerable<Size> a, IEnumerable<Size> b)
        {
            if (a is null || b is null) return b is object;
            ulong ta = a.Aggregate(0UL, (s, i) => s + i.I);
            ulong tb = b.Aggregate(0UL, (s, i) => s + i.I);
            return ta == tb ? IsLessThan(a.Where(i => i.Next != null).SelectMany(i => i.Next),
                                         b.Where(i => i.Next != null).SelectMany(i => i.Next))
                            : ta < tb;
        }
    }

    public interface IGen
    {
        (object, Size) Generate(PCG pcg);
    }

    public abstract class Gen<T> : IGen, IEnumerable<T>
    {
        public abstract (T, Size) Generate(PCG pcg);
        (object, Size) IGen.Generate(PCG pcg)
        {
            return Generate(pcg);
        }
        IEnumerable<T> ToEnumerable()
        {
            var pcg = PCG.ThreadPCG;
            while (true) yield return Generate(pcg).Item1;
        }
        public IEnumerator<T> GetEnumerator() => ToEnumerable().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ToEnumerable().GetEnumerator();
        public GenArray<T> Array => new GenArray<T>(this);
        public GenArray2D<T> Array2D => new GenArray2D<T>(this);
        public GenList<T> List => new GenList<T>(this);
    }

    class GenF<T> : Gen<T>
    {
        readonly Func<PCG, (T, Size)> generate;
        public GenF(Func<PCG, (T, Size)> generate) => this.generate = generate;
        public override (T, Size) Generate(PCG pcg) => generate(pcg);
    }

    public static class Gen
    {
        public static Gen<T> Create<T>(Func<PCG, (T, Size)> gen) => new GenF<T>(gen);
        public static Gen<R> Select<T, R>(this Gen<T> gen, Func<T, R> selector) => new GenF<R>(pcg =>
        {
            var (v, s) = gen.Generate(pcg);
            return (selector(v), s);
        });
        public static Gen<R> Select<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2,
            Func<T1, T2, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            return (selector(v1, v2), new Size(0UL, new[] { s1, s2 }));
        });
        public static Gen<R> Select<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
            Func<T1, T2, T3, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            return (selector(v1, v2, v3), new Size(0UL, new[] { s1, s2, s3 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Func<T1, T2, T3, T4, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            return (selector(v1, v2, v3, v4), new Size(0UL, new[] { s1, s2, s3, s4 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Func<T1, T2, T3, T4, T5, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5), new Size(0UL, new[] { s1, s2, s3, s4, s5 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Func<T1, T2, T3, T4, T5, T6, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var (v6, s6) = gen6.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5, v6), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Func<T1, T2, T3, T4, T5, T6, T7, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var (v6, s6) = gen6.Generate(pcg);
            var (v7, s7) = gen7.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Func<T1, T2, T3, T4, T5, T6, T7, T8, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var (v6, s6) = gen6.Generate(pcg);
            var (v7, s7) = gen7.Generate(pcg);
            var (v8, s8) = gen8.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7, v8), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7, s8 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9,
            Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var (v6, s6) = gen6.Generate(pcg);
            var (v7, s7) = gen7.Generate(pcg);
            var (v8, s8) = gen8.Generate(pcg);
            var (v9, s9) = gen9.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7, v8, v9), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
            Gen<T4> gen4, Gen<T5> gen5, Gen<T6> gen6, Gen<T7> gen7, Gen<T8> gen8, Gen<T9> gen9, Gen<T10> gen10,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, R> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var (v6, s6) = gen6.Generate(pcg);
            var (v7, s7) = gen7.Generate(pcg);
            var (v8, s8) = gen8.Generate(pcg);
            var (v9, s9) = gen9.Generate(pcg);
            var (v10, s10) = gen10.Generate(pcg);
            return (selector(v1, v2, v3, v4, v5, v6, v7, v8, v9, v10), new Size(0UL, new[] { s1, s2, s3, s4, s5, s6, s7, s8, s9, s10 }));
        });
        public static Gen<(T0 V0, T1 V1)> Select<T0, T1>(this Gen<T0> gen0, Gen<T1> gen1) => new GenF<(T0, T1)>(pcg =>
        {
            var (v0, s0) = gen0.Generate(pcg);
            var (v1, s1) = gen1.Generate(pcg);
            return ((v0, v1), new Size(0UL, new[] { s0, s1 }));
        });
        public static Gen<(T0 V0, T1 V1, T2 V2)> Select<T0, T1, T2>(this Gen<T0> gen0, Gen<T1> gen1, Gen<T2> gen2)
            => new GenF<(T0, T1, T2)>(pcg =>
        {
            var (v0, s0) = gen0.Generate(pcg);
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            return ((v0, v1, v2), new Size(0UL, new[] { s0, s1, s2 }));
        });
        public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3)> Select<T0, T1, T2, T3>(this Gen<T0> gen0, Gen<T1> gen1,
            Gen<T2> gen2, Gen<T3> gen3) => new GenF<(T0, T1, T2, T3)>(pcg =>
        {
            var (v0, s0) = gen0.Generate(pcg);
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            return ((v0, v1, v2, v3), new Size(0UL, new[] { s0, s1, s2, s3 }));
        });
        public static Gen<(T0 V0, T1 V1, T2 V2, T3 V3, T4 V4)> Select<T0, T1, T2, T3, T4>(this Gen<T0> gen0, Gen<T1> gen1,
            Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4) => new GenF<(T0, T1, T2, T3, T4)>(pcg =>
        {
            var (v0, s0) = gen0.Generate(pcg);
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            return ((v0, v1, v2, v3, v4), new Size(0UL, new[] { s0, s1, s2, s3, s4 }));
        });
        public static Gen<R> SelectMany<T, R>(this Gen<T> gen, Func<T, Gen<R>> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen.Generate(pcg);
            var genR = selector(v1);
            var (vR, sR) = genR.Generate(pcg);
            return (vR, new Size(s1.I, new[] { sR }));
        });
        public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Gen<T2> gen2, Func<T1, T2, Gen<R>> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var genR = selector(v1, v2);
            var (vR, sR) = genR.Generate(pcg);
            return (vR, new Size(s1.I + s2.I, new[] { sR }));
        });
        public static Gen<R> SelectMany<T1, T2, T3, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3,
            Func<T1, T2, T3, Gen<R>> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var genR = selector(v1, v2, v3);
            var (vR, sR) = genR.Generate(pcg);
            return (vR, new Size(s1.I + s2.I + s3.I, new[] { sR }));
        });
        public static Gen<R> SelectMany<T1, T2, T3, T4, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Func<T1, T2, T3, T4, Gen<R>> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var genR = selector(v1, v2, v3, v4);
            var (vR, sR) = genR.Generate(pcg);
            return (vR, new Size(s1.I + s2.I + s3.I +s4.I, new[] { sR }));
        });
        public static Gen<R> SelectMany<T1, T2, T3, T4, T5, R>(this Gen<T1> gen1, Gen<T2> gen2, Gen<T3> gen3, Gen<T4> gen4,
            Gen<T5> gen5, Func<T1, T2, T3, T4, T5, Gen<R>> selector) => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            var (v5, s5) = gen5.Generate(pcg);
            var genR = selector(v1, v2, v3, v4, v5);
            var (vR, sR) = genR.Generate(pcg);
            return (vR, new Size(s1.I + s2.I + s3.I + s4.I + s5.I, new[] { sR }));
        });
        public static Gen<R> SelectMany<T1, T2, R>(this Gen<T1> gen1, Func<T1, Gen<T2>> genSelector, Func<T1, T2, R> resultSelector)
            => new GenF<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var gen2 = genSelector(v1);
            var (v2, s2) = gen2.Generate(pcg);
            return (resultSelector(v1, v2), new Size(s1.I, new[] { s2 }));
        });
        public static Gen<T> Where<T>(this Gen<T> gen, Func<T, bool> predicate) => new GenF<T>(pcg =>
        {
            var t = gen.Generate(pcg);
            while (!predicate(t.Item1)) t = gen.Generate(pcg);
            return t;
        });
        static readonly Size zero = new Size(0UL, null);
        public static Gen<T> Const<T>(T value) => new GenF<T>(_ => (value, zero));
        public static Gen<T> OneOf<T>(params T[] ts) => Int[0, ts.Length - 1].Select(i => ts[i]);
        public static Gen<T> OneOf<T>(params Gen<T>[] gens) => Int[0, gens.Length - 1].SelectMany(i => gens[i]);
        public static Gen<T> Enum<T>() where T : Enum
        {
            var a = System.Enum.GetValues(typeof(T));
            var ts = new T[a.Length];
            for (int i = 0; i < ts.Length; i++)
                ts[i] = (T)a.GetValue(i);
            return OneOf(ts);
        }
        public static Gen<T> Cast<T>(this IGen gen) => new GenF<T>(pcg => {
            var (o, s) = gen.Generate(pcg);
            return ((T)o, s);
        });
        public static Gen<T> Frequency<T>(params (int, T)[] ts)
        {
            ts = ((int, T)[])ts.Clone();
            int total = 0;
            for (int i = 0; i < ts.Length; i++)
            {
                total += ts[i].Item1;
                ts[i].Item1 = total;
            }
            return Int[1, total].Select(c =>
            {
                for (int i = 0; i < ts.Length - 1; i++)
                    if (c <= ts[i].Item1)
                        return ts[i].Item2;
                return ts[ts.Length - 1].Item2;
            });
        }
        public static Gen<T> Frequency<T>(params (int, Gen<T>)[] gens)
        {
            gens = ((int, Gen<T>)[])gens.Clone();
            int total = 0;
            for (int i = 0; i < gens.Length; i++)
            {
                total += gens[i].Item1;
                gens[i].Item1 = total;
            }
            return Int[1, total].SelectMany(c =>
            {
                for (int i = 0; i < gens.Length - 1; i++)
                    if (c <= gens[i].Item1)
                        return gens[i].Item2;
                return gens[gens.Length - 1].Item2;
            });
        }

        public static readonly GenBool Bool = new GenBool();
        public static readonly GenSByte SByte = new GenSByte();
        public static readonly GenByte Byte = new GenByte();
        public static readonly GenShort Short = new GenShort();
        public static readonly GenUShort UShort = new GenUShort();
        public static readonly GenInt Int = new GenInt();
        public static readonly GenUInt UInt = new GenUInt();
        public static readonly GenLong Long = new GenLong();
        public static readonly GenULong ULong = new GenULong();
        public static readonly GenFloat Float = new GenFloat();
        public static readonly GenFloat Single = Float;
        public static readonly GenDouble Double = new GenDouble();
        public static readonly GenDecimal Decimal = new GenDecimal();
        public static readonly GenDateTime DateTime = new GenDateTime();
        public static readonly GenChar Char = new GenChar();
        public static readonly GenString String = new GenString();
    }

    public class GenBool : Gen<bool>
    {
        public override (bool, Size) Generate(PCG pcg)
        {
            uint i = pcg.Next();
            return ((i & 1u) == 1u, new Size(i, null));
        }
    }

    public class GenSByte : Gen<sbyte>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Zigzag(sbyte i) => (ulong)((i << 1) ^ (i >> 7));
        public override (sbyte, Size) Generate(PCG pcg)
        {
            sbyte i = (sbyte)(pcg.Next() & 255u);
            return (i, new Size(Zigzag(i), null));
        }
        public Gen<sbyte> this[sbyte start, sbyte finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new GenF<sbyte>(pcg =>
                {
                    sbyte i = (sbyte)(start + pcg.Next(l));
                    return (i, new Size(Zigzag(i), null));
                });
            }
        }
    }

    public class GenByte : Gen<byte>
    {
        public override (byte, Size) Generate(PCG pcg)
        {
            byte i = (byte)(pcg.Next() & 255u);
            return (i, new Size(i, null));
        }
        public Gen<byte> this[byte start, byte finish]
        {
            get
            {
                uint s = start;
                uint l = finish - s + 1u;
                return new GenF<byte>(pcg =>
                {
                    byte i = (byte)(s + pcg.Next(l));
                    return (i, new Size(i, null));
                });
            }
        }
    }

    public class GenShort : Gen<short>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Zigzag(short i) => (ulong)((i << 1) ^ (i >> 15));
        public override (short, Size) Generate(PCG pcg)
        {
            short i = (short)(pcg.Next() & 65535u);
            return (i, new Size(Zigzag(i), null));
        }
        public Gen<short> this[short start, short finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new GenF<short>(pcg =>
                {
                    short i = (short)(start + pcg.Next(l));
                    return (i, new Size(Zigzag(i), null));
                });
            }
        }
    }

    public class GenUShort : Gen<ushort>
    {
        public override (ushort, Size) Generate(PCG pcg)
        {
            ushort i = (ushort)(pcg.Next() & 65535u);
            return (i, new Size(i, null));
        }
        public Gen<ushort> this[ushort start, ushort finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new GenF<ushort>(pcg =>
                {
                    ushort i = (ushort)(start + pcg.Next(l));
                    return (i, new Size(i, null));
                });
            }
        }
    }

    public class GenInt : Gen<int>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Zigzag(int i) => (uint)((i << 1) ^ (i >> 31));
        public override (int, Size) Generate(PCG pcg)
        {
            int i = (int)pcg.Next();
            return (i, new Size(Zigzag(i), null));
        }
        public Gen<int> this[int start, int finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new GenF<int>(pcg =>
                {
                    int i = (int)(start + pcg.Next(l));
                    return (i, new Size(Zigzag(i), null));
                });
            }
        }
    }

    public class GenUInt : Gen<uint>
    {
        public override (uint, Size) Generate(PCG pcg)
        {
            uint i = pcg.Next();
            return (i, new Size(i, null));
        }
        public Gen<uint> this[uint start, uint finish]
        {
            get
            {
                uint l = finish - start + 1u;
                return new GenF<uint>(pcg =>
                {
                    uint i = start + pcg.Next(l);
                    return (i, new Size(i, null));
                });
            }
        }
    }

    public class GenLong : Gen<long>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Zigzag(long i) => (ulong)((i << 1) ^ (i >> 63));

        public override (long, Size) Generate(PCG pcg)
        {
            long i = (long)pcg.Next64();
            return (i, new Size(Zigzag(i), null));
        }
        public Gen<long> this[long start, long finish]
        {
            get
            {

                ulong l = (ulong)(finish - start) + 1ul;
                return new GenF<long>(pcg =>
                {
                    long i = start + (long)pcg.Next64(l);
                    return (i, new Size(Zigzag(i), null));
                });
            }
        }
    }

    public class GenULong : Gen<ulong>
    {
        public override (ulong, Size) Generate(PCG pcg)
        {
            ulong i = pcg.Next64();
            return (i, new Size(i, null));
        }
        public Gen<ulong> this[ulong start, ulong finish]
        {
            get
            {
                ulong l = finish - start + 1ul;
                return new GenF<ulong>(pcg =>
                {
                    ulong i = start + pcg.Next64(l);
                    return (i, new Size(i, null));
                });
            }
        }
    }

    public class GenFloat : Gen<float>
    {
        [StructLayout(LayoutKind.Explicit)]
        struct Converter
        {
            [FieldOffset(0)] public uint I;
            [FieldOffset(0)] public float F;
        }
        public override (float, Size) Generate(PCG pcg)
        {
            uint i = pcg.Next();
            return (new Converter { I = i }.F, new Size(i, null));
        }
        public Gen<float> this[float start, float finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new GenF<float>(pcg =>
                {
                    uint i = pcg.Next() >> 9;
                    return (new Converter { I = i | 0x3F800000 }.F * finish + start
                            , new Size(i, null));
                });
            }
        }
        public Gen<float> Unit = new GenF<float>(pcg =>
        {
            uint i = pcg.Next() >> 9;
            return (new Converter { I = i | 0x3F800000 }.F - 1f, new Size(i, null));
        });
        public Gen<float> Normal = new GenF<float>(pcg =>
        {
            uint i = pcg.Next();
            return ((i & 0x7F800000U) == 0x7F800000U ? (8f-(i & 0xFU))
                    : new Converter { I = i }.F
                , new Size(i, null));
        });
        static float MakeSpecial(uint i)
        {
            switch (i & 0xFU)
            {
                case 0x0U: return float.NaN;
                case 0x1U: return float.PositiveInfinity;
                case 0x2U: return float.NegativeInfinity;
                case 0x3U: return float.MaxValue;
                case 0x4U: return float.MinValue;
                case 0x5U: return float.Epsilon;
                case 0x6U: return -float.Epsilon;
                case 0x7U: return 1f;
                case 0x8U: return -1f;
                case 0x9U: return 2f;
                case 0xAU: return -2f;
                case 0xBU: return 3f;
                case 0xCU: return -3f;
                case 0xDU: return 4f;
                case 0xEU: return -4f;
                default: return 0f;
            }
        }
        public Gen<float> Special = new GenF<float>(pcg =>
        {
            uint i = pcg.Next();
            return ((i & 0xF0U) == 0xD0U ? MakeSpecial(i)
                    : new Converter { I = i }.F
                , new Size(i, null));
        });
    }

    public class GenDouble : Gen<double>
    {
        public override (double, Size) Generate(PCG pcg)
        {
            ulong i = pcg.Next64();
            return (BitConverter.Int64BitsToDouble((long)i), new Size(i, null));
        }
        public Gen<double> this[double start, double finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new GenF<double>(pcg =>
                {
                    ulong i = pcg.Next64() >> 12;
                    return (BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * finish + start
                            , new Size(i, null));
                });
            }
        }
        public Gen<double> Unit = new GenF<double>(pcg =>
        {
            ulong i = pcg.Next64() >> 12;
            return (BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1.0
                    , new Size(i, null));
        });
        public Gen<double> Normal = new GenF<double>(pcg =>
        {
            ulong i = pcg.Next64();
            return ((i & 0x7FF0000000000000U) == 0x7FF0000000000000U ? (8.0 - (i & 0xFUL))
                  : BitConverter.Int64BitsToDouble((long)i)
                , new Size(i, null));
        });
        static double MakeSpecial(ulong i)
        {
            switch (i & 0xFUL)
            {
                case 0x0UL: return double.NaN;
                case 0x1UL: return double.PositiveInfinity;
                case 0x2UL: return double.NegativeInfinity;
                case 0x3UL: return double.MaxValue;
                case 0x4UL: return double.MinValue;
                case 0x5UL: return double.Epsilon;
                case 0x6UL: return -double.Epsilon;
                case 0x7UL: return 1.0;
                case 0x8UL: return -1.0;
                case 0x9UL: return 2.0;
                case 0xAUL: return -2.0;
                case 0xBUL: return 3.0;
                case 0xCUL: return -3.0;
                case 0xDUL: return 4.0;
                case 0xEUL: return -4.0;
                default: return 0.0;
            }
        }
        public Gen<double> Special = new GenF<double>(pcg =>
        {
            ulong i = pcg.Next64();
            return ((i & 0xF0UL) == 0xD0UL ? MakeSpecial(i)
                    : BitConverter.Int64BitsToDouble((long)i)
                , new Size(i, null));
        });
    }

    public class GenDecimal : Gen<decimal>
    {
        [StructLayout(LayoutKind.Explicit)]
        struct Converter
        {
            [FieldOffset(0)] public ulong I0;
            [FieldOffset(8)] public ulong I1;
            [FieldOffset(0)] public decimal D;
        }
        public override (decimal, Size) Generate(PCG pcg)
        {
            var c = new Converter { I0 = pcg.Next64(), I1 = pcg.Next64() };
            return (c.D, new Size(c.I0, null));
        }
        public Gen<decimal> this[decimal start, decimal finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new GenF<decimal>(pcg =>
                {
                    var i = pcg.Next64() >> 12;
                    return ((decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) * finish + start
                            , new Size(i, null));
                });
            }
        }
        public Gen<decimal> Unit = new GenF<decimal>(pcg =>
        {
            ulong i = pcg.Next64() >> 12;
            return ((decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000) - 1M
                    , new Size(i, null));
        });
    }

    public class GenDateTime : Gen<DateTime>
    {
        readonly ulong max = (ulong)DateTime.MaxValue.Ticks;
        public override (DateTime, Size) Generate(PCG pcg)
        {
            ulong i = pcg.Next64(max);
            return (new DateTime((long)i), new Size(i, null));
        }
        public Gen<DateTime> this[DateTime start, DateTime finish]
        {
            get
            {
                ulong l = (ulong)(finish.Ticks - start.Ticks) + 1ul;
                return new GenF<DateTime>(pcg =>
                {
                    ulong i = (ulong)start.Ticks + pcg.Next64(l);
                    return (new DateTime((long)i), new Size(i, null));
                });
            }
        }
    }

    public class GenChar : Gen<char>
    {
        public override (char, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 127u;
            return ((char)i, new Size(i, null));
        }
        public Gen<char> this[char start, char finish]
        {
            get
            {
                uint s = start;
                uint l = finish + 1u - s;
                return new GenF<char>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((char)(s + i), new Size(i, null));
                });
            }
        }
        public Gen<char> this[string chars]
        {
            get
            {
                char[] charArray = chars.ToArray();
                return new GenF<char>(pcg =>
                {
                    var i = pcg.Next((uint)charArray.Length);
                    return (charArray[i], new Size(i, null));
                });
            }
        }
    }

    public class GenString : Gen<string>
    {
        static readonly Gen<string> d = Gen.Char.Array.Select(i => new string(i));
        public override (string, Size) Generate(PCG pcg) => d.Generate(pcg);
        public Gen<string> this[int start, int finish] =>
            Gen.Char.Array[start, finish].Select(i => new string(i));
        public Gen<string> this[Gen<char> gen, int start, int finish] =>
            gen.Array[start, finish].Select(i => new string(i));
    }

    public class GenArray<T> : Gen<T[]>
    {
        readonly Gen<T> gen;
        public GenArray(Gen<T> gen) => this.gen = gen;
        public override (T[], Size) Generate(PCG pcg)
        {
            var l = pcg.Next() & 127U;
            var vs = new T[l];
            var ss = new Size[l];
            for (int i = 0; i < vs.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(l, ss));
        }
        public Gen<T[]> this[Gen<int> length] => new GenF<T[]>(pcg =>
        {
            var (l, sl) = length.Generate(pcg);
            var vs = new T[l];
            var ss = new Size[l];
            for (int i = 0; i < vs.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(sl.I, ss));
        });
        public Gen<T[]> this[int length] => new GenF<T[]>(pcg =>
        {
            var vs = new T[length];
            var ss = new Size[length];
            for (int i = 0; i < vs.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(0L, ss));
        });
        public Gen<T[]> this[int start, int finish] => this[Gen.Int[start, finish]];

    }

    public class GenArray2D<T> : Gen<T[,]>
    {
        readonly Gen<T> gen;
        public GenArray2D(Gen<T> gen) => this.gen = gen;
        (T[,], Size) Generate(PCG pcg, uint length0, uint length1)
        {
            var vs = new T[length0, length1];
            for (int i = 0; i < length0; i++)
                for (int j = 0; j < length1; j++)
                {
                    var v = gen.Generate(pcg).Item1;
                    vs[i, j] = v;
                }
            return (vs, new Size(length0 + length1, null));
        }
        public override (T[,], Size) Generate(PCG pcg)
        {
            return Generate(pcg, pcg.Next() & 127U, pcg.Next() & 127U);
        }
        public Gen<T[,]> this[int length0, int length1] => new GenF<T[,]>(pcg =>
        {
            return Generate(pcg, (uint)length0, (uint)length1);
        });
        public Gen<T[,]> this[Gen<int> length0, Gen<int> length1] => new GenF<T[,]>(pcg =>
        {
            var l0 = length0.Generate(pcg).Item1;
            var l1 = length1.Generate(pcg).Item1;
            return Generate(pcg, (uint)l0, (uint)l1);
        });
    }

    public class GenList<T> : Gen<List<T>>
    {
        readonly Gen<T> gen;
        public GenList(Gen<T> gen) => this.gen = gen;
        public override (List<T>, Size) Generate(PCG pcg)
        {
            var l = pcg.Next() & 127U;
            var vs = new List<T>((int)l);
            var ss = new Size[l];
            for (int i = 0; i < ss.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs.Add(v);
                ss[i] = s;
            }
            return (vs, new Size(l, ss));
        }
        public Gen<List<T>> this[Gen<int> length] => new GenF<List<T>>(pcg =>
        {
            var (l, sl) = length.Generate(pcg);
            var vs = new List<T>(l);
            var ss = new Size[l];
            for (int i = 0; i < ss.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs.Add(v);
                ss[i] = s;
            }
            return (vs, new Size(sl.I, ss));
        });
        public Gen<List<T>> this[int length] => new GenF<List<T>>(pcg =>
        {
            var vs = new List<T>(length);
            var ss = new Size[length];
            for (int i = 0; i < ss.Length; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs.Add(v);
                ss[i] = s;
            }
            return (vs, new Size(0L, ss));
        });
        public Gen<List<T>> this[int start, int finish] => this[Gen.Int[start, finish]];
    }
}