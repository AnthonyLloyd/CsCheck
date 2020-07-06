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

    public interface IGen<T>
    {
        public (T, Size) Generate(PCG pcg);
    }

    public struct Gen<T> : IGen<T>
    {
        readonly Func<PCG, (T, Size)> generate;
        public Gen(Func<PCG, (T, Size)> generate) => this.generate = generate;
        public (T, Size) Generate(PCG pcg) => generate(pcg);
    }

    public struct GenBool : IGen<bool>
    {
        public (bool, Size) Generate(PCG pcg)
        {
            var i = pcg.Next();
            return ((i & 1u) == 1u, new Size(i, Array.Empty<Size>()));
        }
    }

    public struct GenSByte : IGen<sbyte>
    {
        public (sbyte, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 255u;
            return ((sbyte)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<sbyte> this[sbyte start, sbyte finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new Gen<sbyte>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((sbyte)(start + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenByte : IGen<byte>
    {
        public (byte, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 255u;
            return ((byte)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<byte> this[byte start, byte finish]
        {
            get
            {
                uint s = start;
                uint l = finish + 1u - s;
                return new Gen<byte>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((byte)(s + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenShort : IGen<short>
    {
        public (short, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 65535u;
            return ((short)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<short> this[short start, short finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new Gen<short>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((short)(start + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenUShort : IGen<ushort>
    {
        public (ushort, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 65535u;
            return ((ushort)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<ushort> this[ushort start, ushort finish]
        {
            get
            {
                uint l = (uint)(finish - start) + 1u;
                return new Gen<ushort>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((ushort)(start + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenInt : IGen<int>
    {
        public (int, Size) Generate(PCG pcg)
        {
            var i = pcg.Next();
            return ((int)i, new Size(i, Array.Empty<Size>()));
        }
        static readonly Gen<int> int128 = new Gen<int>(pcg =>
        {
            var i = pcg.Next() & 127u;
            return ((int)i, new Size(i, Array.Empty<Size>()));
        });
        public Gen<int> this[int start, int finish]
        {
            get
            {
                if (start == finish) return Gen.Const(start);
                if (finish == 127 && start == 0) return int128;
                uint l = (uint)(finish - start) + 1u;
                return new Gen<int>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((int)(start + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenUInt : IGen<uint>
    {
        public (uint, Size) Generate(PCG pcg)
        {
            var i = pcg.Next();
            return (i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<uint> this[uint start, uint finish]
        {
            get
            {
                uint l = finish - start + 1u;
                return new Gen<uint>(pcg =>
                {
                    var i = pcg.Next(l);
                    return (start + i, new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenLong : IGen<long>
    {
        public (long, Size) Generate(PCG pcg)
        {
            var i = pcg.Next64();
            return ((long)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<long> this[long start, long finish]
        {
            get
            {
                ulong l = (ulong)(finish - start) + 1ul;
                return new Gen<long>(pcg =>
                {
                    var i = pcg.Next64(l);
                    return ((long)((ulong)start+i), new Size(i, Array.Empty<Size>())); // long / ulong logic is bad needs tests
                });
            }
        }
    }

    public struct GenULong : IGen<ulong>
    {
        public (ulong, Size) Generate(PCG pcg)
        {
            var i = pcg.Next64();
            return (i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<ulong> this[ulong start, ulong finish]
        {
            get
            {
                ulong l = finish - start + 1ul;
                return new Gen<ulong>(pcg =>
                {
                    var i = pcg.Next64(l);
                    return (start + i, new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenFloat : IGen<float>
    {
        public (float, Size) Generate(PCG pcg)
        {
            var i = pcg.Next64() >> 12;
            var f = (float)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L); // can do as single with only Next32, test
            return (-300.0f + f * 200.0f, new Size(i, Array.Empty<Size>()));
        }
        public Gen<float> this[float start, float finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new Gen<float>(pcg =>
                {
                    var i = pcg.Next64() >> 12;
                    var f = (float)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L);
                    return (start + f * finish, new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenDouble : IGen<double>
    {
        public (double, Size) Generate(PCG pcg)
        {
            var i = pcg.Next64() >> 12;
            var f = BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L);
            return (-300.0 + f * 200.0, new Size(i, Array.Empty<Size>()));
        }
        public Gen<double> this[double start, double finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new Gen<double>(pcg =>
                {
                    var i = pcg.Next64() >> 12;
                    var f = BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L);
                    return (start + f * finish, new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenDecimal : IGen<decimal>
    {
        public (decimal, Size) Generate(PCG pcg)
        {
            var i = pcg.Next64() >> 12;
            var f = (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L);
            return (-300.0M + f * 200.0M, new Size(i, Array.Empty<Size>()));
        }
        public Gen<decimal> this[decimal start, decimal finish]
        {
            get
            {
                finish -= start;
                start -= finish;
                return new Gen<decimal>(pcg =>
                {
                    var i = pcg.Next64() >> 12;
                    var f = (decimal)BitConverter.Int64BitsToDouble((long)i | 0x3FF0000000000000L);
                    return (start + f * finish, new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenChar : IGen<char>
    {
        public (char, Size) Generate(PCG pcg)
        {
            var i = pcg.Next() & 127u;
            return ((char)i, new Size(i, Array.Empty<Size>()));
        }
        public Gen<char> this[char start, char finish]
        {
            get
            {
                uint s = start;
                uint l = finish + 1u - s;
                return new Gen<char>(pcg =>
                {
                    var i = pcg.Next(l);
                    return ((char)(s + i), new Size(i, Array.Empty<Size>()));
                });
            }
        }
    }

    public struct GenString : IGen<string>
    {
        static readonly Gen<string> d = Gen.Char.Array(0, 127).Select(i => new string (i));
        public (string, Size) Generate(PCG pcg)
        {
            return d.Generate(pcg);
        }
        public Gen<string> this[int start, int finish]
        {
            get
            {
                return Gen.Char.Array(start, finish).Select(i => new string(i));
            }
        }
    }

    public static class Gen
    {
        public static Gen<R> Select<T, R>(this IGen<T> gen, Func<T, R> selector) => new Gen<R>(pcg =>
        {
            var (v, s) = gen.Generate(pcg);
            return (selector(v), s);
        });
        public static Gen<R> Select<T1, T2, R>(this IGen<T1> gen1, IGen<T2> gen2,
            Func<T1, T2, R> selector) => new Gen<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            return (selector(v1, v2), new Size(0UL, new[] { s1, s2 }));
        });
        public static Gen<R> Select<T1, T2, T3, R>(this IGen<T1> gen1, IGen<T2> gen2, IGen<T3> gen3,
            Func<T1, T2, T3, R> selector) => new Gen<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            return (selector(v1, v2, v3), new Size(0UL, new[] { s1, s2, s3 }));
        });
        public static Gen<R> Select<T1, T2, T3, T4, R>(this IGen<T1> gen1, IGen<T2> gen2, IGen<T3> gen3, IGen<T4> gen4,
            Func<T1, T2, T3, T4, R> selector) => new Gen<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            var (v3, s3) = gen3.Generate(pcg);
            var (v4, s4) = gen4.Generate(pcg);
            return (selector(v1, v2, v3, v4), new Size(0UL, new[] { s1, s2, s3, s4 }));
        });
        public static Gen<R> SelectMany<T, R>(this IGen<T> gen, Func<T, IGen<R>> selector) => new Gen<R>(pcg =>
        {
            var (v1, s1) = gen.Generate(pcg);
            var gen2 = selector(v1);
            var (v2, s2) = gen2.Generate(pcg);
            return (v2, new Size(s1.I, new[] { s2 }));
        });
        public static Gen<R> SelectMany<T1, T2, R>(this IGen<T1> gen1, Func<T1, IGen<T2>> genSelector, Func<T1, T2, R> resultSelector)
            => new Gen<R>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var gen2 = genSelector(v1);
            var (v2, s2) = gen2.Generate(pcg);
            return (resultSelector(v1, v2), new Size(s1.I, new[] { s2 }));
        });
        public static Gen<(T1, T2)> Tuple<T1, T2>(this IGen<T1> gen1, IGen<T2> gen2) => new Gen<(T1, T2)>(pcg =>
        {
            var (v1, s1) = gen1.Generate(pcg);
            var (v2, s2) = gen2.Generate(pcg);
            return ((v1, v2), new Size(0UL, new[] { s1, s2 }));
        });
        public static Gen<T[]> Array<T>(this IGen<T> gen, IGen<int> genLength) => new Gen<T[]>(pcg =>
        {
            var (l, sl) = genLength.Generate(pcg);
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
        public static Gen<T[]> Array<T>(this IGen<T> gen, int start, int finish) => Array(gen, Int[start, finish]);
        public static Gen<T[]> Array<T>(this IGen<T> gen, int length) => Array(gen, Const(length));
        public static Gen<List<T>> List<T>(this IGen<T> gen, IGen<int> genLength) => new Gen<List<T>>(pcg =>
        {
            var (l, sl) = genLength.Generate(pcg);
            var vs = new List<T>(l);
            var ss = new Size[l];
            for (int i = 0; i < l; i++)
            {
                var (v, s) = gen.Generate(pcg);
                vs[i] = v;
                ss[i] = s;
            }
            return (vs, new Size(sl.I, ss));
        });
        public static Gen<List<T>> List<T>(this IGen<T> gen, int start, int finish) => List(gen, Int[start,finish]);
        public static Gen<List<T>> List<I,T>(this IGen<T> gen, int length) => List(gen, Const(length));
        static readonly Size zero = new Size(0UL, System.Array.Empty<Size>());
        public static Gen<T> Const<T>(T value) => new Gen<T>(_ => (value, zero));
        public static Gen<T> OneOf<T>(List<IGen<T>> gens) => Int[0, gens.Count].SelectMany(i => gens[i]);
        public static readonly GenSByte SByte = new GenSByte();
        public static readonly GenByte Byte = new GenByte();
        public static readonly GenShort Short = new GenShort();
        public static readonly GenUShort UShort = new GenUShort();
        public static readonly GenInt Int = new GenInt();
        public static readonly GenUInt UInt = new GenUInt();
        public static readonly GenLong Long = new GenLong();
        public static readonly GenULong ULong = new GenULong();
        public static readonly GenFloat Float = new GenFloat();
        public static readonly GenDouble Double = new GenDouble();
        public static readonly GenDecimal Decimal = new GenDecimal();
        public static readonly GenChar Char = new GenChar();
        public static readonly GenString String = new GenString();
    }
}