using System;

namespace CsCheck
{
    public static class Check
    {
        public static void Assert<T>(this IGen<T> gen, Action<T> action)
        {
            var pcg = new PCG(101);
            ulong state;
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    state = pcg.State;
                    var (v, s) = gen.Generate(pcg);
                    action(v);
                }
            }
            catch(Exception)
            {
                throw;
            }
        }
        public static void Assert<T>(this IGen<T> gen, Func<T, bool> action)
        {
            var pcg = new PCG(101);
            ulong state;
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    state = pcg.State;
                    var (v, s) = gen.Generate(pcg);
                    if (!action(v)) throw new Exception("hi");
                }
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
