using System;

namespace CsCheck
{
    public class CsCheckException : Exception
    {
        public CsCheckException(string message) : base(message) { }
    }

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
            double mean = expected.Length - 1;
            double sdev = Math.Sqrt(2 * mean);
            double SDs = (chi - mean) / sdev;
            if (Math.Abs(SDs) > 6.0) throw new CsCheckException("Chi-squared standard deviation = " + SDs.ToString("0.0"));
        }
    }
}
