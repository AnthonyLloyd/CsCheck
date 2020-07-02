using System;

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

    public abstract class Gen<T>
    {
        public abstract (T, Size) Generate(PCG pcg);
    }

    class GenByte : Gen<byte>
    {
        public static Gen<byte> Instance = new GenByte();
        public override (byte, Size) Generate(PCG pcg)
        {
            var i = pcg.Next(256);
            return ((byte)i, new Size((ulong)i, Array.Empty<Size>()));
        }
    }

    class GenMap<A, B> : Gen<B>
    {
        readonly Gen<A> Gen;
        readonly Func<A, B> F;
        internal GenMap(Gen<A> gen, Func<A, B> f)
        {
            Gen = gen;
            F = f;
        }
        public override (B, Size) Generate(PCG pcg)
        {
            var (a, s) = Gen.Generate(pcg);
            return (F(a), s);
        }
    }

    class GenMap2<A, B, C> : Gen<C>
    {
        readonly Gen<A> GenA;
        readonly Gen<B> GenB;
        readonly Func<A, B, C> F;
        internal GenMap2(Gen<A> genA, Gen<B> genB, Func<A, B, C> f)
        {
            GenA = genA;
            GenB = genB;
            F = f;
        }
        public override (C, Size) Generate(PCG pcg)
        {
            var (a, sa) = GenA.Generate(pcg);
            var (b, sb) = GenB.Generate(pcg);
            return (F(a, b), new Size(0UL, new[] { sa, sb }));
        }
    }

    class GenMap3<A, B, C, D> : Gen<D>
    {
        readonly Gen<A> GenA;
        readonly Gen<B> GenB;
        readonly Gen<C> GenC;
        readonly Func<A, B, C, D> F;
        internal GenMap3(Gen<A> genA, Gen<B> genB, Gen<C> genC, Func<A, B, C, D> f)
        {
            GenA = genA;
            GenB = genB;
            GenC = genC;
            F = f;
        }
        public override (D, Size) Generate(PCG pcg)
        {
            var (a, sa) = GenA.Generate(pcg);
            var (b, sb) = GenB.Generate(pcg);
            var (c, sc) = GenC.Generate(pcg);
            return (F(a, b, c), new Size(0UL, new[] { sa, sb, sc }));
        }
    }

    class GenBind<A, B> : Gen<B>
    {
        readonly Gen<A> Gen;
        readonly Func<A, Gen<B>> F;
        internal GenBind(Gen<A> gen, Func<A, Gen<B>> f)
        {
            Gen = gen;
            F = f;
        }
        public override (B, Size) Generate(PCG pcg)
        {
            var (a, _) = Gen.Generate(pcg);
            return F(a).Generate(pcg);
        }
    }

    public static class Gen
    {
        public static Gen<B> Select<A, B>(this Gen<A> gen, Func<A, B> f) => new GenMap<A, B>(gen, f);
        public static Gen<C> Select<A, B, C>(this Gen<A> genA, Gen<B> genB, Func<A, B, C> f) => new GenMap2<A, B, C>(genA, genB, f);
        public static Gen<D> Select<A, B, C, D>(this Gen<A> genA, Gen<B> genB, Gen<C> genC, Func<A, B, C, D> f) => new GenMap3<A, B, C, D>(genA, genB, genC, f);
        public static Gen<B> SelectMany<A, B>(this Gen<A> gen, Func<A, Gen<B>> f) => new GenBind<A, B>(gen, f);
        public static Gen<C> SelectMany<A, B, C>(this Gen<A> gen, Func<A, Gen<B>> c, Func<A,B,C> f) =>
            new GenBind<A, C>(gen, a => new GenMap<B,C>(c(a), b => f(a,b)));
        public static Gen<byte> Byte() => GenByte.Instance;
    }
}