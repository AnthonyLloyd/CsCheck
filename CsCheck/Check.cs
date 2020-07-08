using System;
using System.Runtime.CompilerServices;

// TODO:
// Can cmd line parameters be passed to here?
// Check ReadyToRun
// Size compare
// ThreadLocal PCG
// Check tests/examples

namespace CsCheck
{
    public class CsCheckException : Exception
    {
        public CsCheckException(string message) : base(message) { }
    }

    public static class Check
    {
        public static int SampleSize = 100;
        public static void Sample<T>(this IGen<T> gen, Action<T> action = null, string seed = null, int size = -1, int threads = -1)
        {
            var pcg = seed is null ? new PCG(101) : PCG.Parse(seed);
            ulong state;
            try
            {
                int l = size == -1 ? SampleSize : size;
                for (int i = 0; i < l; i++)
                {
                    state = pcg.State;
                    var (v, s) = gen.Generate(pcg);
                    action(v);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public static void Sample<T>(this IGen<T> gen, Func<T, bool> action, string seed = null, int size = -1, int threads = -1)
        {
            var pcg = seed is null ? new PCG(101) : PCG.Parse(seed);
            ulong state;
            try
            {
                int l = size == -1 ? SampleSize : size;
                for (int i = 0; i < l; i++)
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

        public static void Faster<T>(Action faster, Action slower, int threads = -1)
        {

        }

        public static void Faster<T>(Func<T> faster, Func<T> slower, int threads = -1)
        {

        }

        public static void Faster<T>(this IGen<T> gen, Action<T> faster, Action<T> slower, int threads = -1)
        {

        }

        public static void Faster<T1, T2>(this IGen<T1> gen, Func<T1, T2> faster, Func<T1, T2> slower, int threads = -1)
        {

        }
    }

    internal class MedianEstimator
    {
        int N, n2 = 2, n3 = 3, n4 = 4;
        double q1, q2, q3, q4, q5;
        internal double Median => q3;
        internal double MADless => q3 - q2;
        internal double MADmore => q4 - q3;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Adjust(double p, int n1, ref int n2, int n3, double q1, ref double q2, double q3)
        {
            double d = N * p - n2;
            if ((d >= 1.0 && n3 - n2 > 1) || (d <= -1.0 && n1 - n2 < -1))
            {
                int ds = Math.Sign(d);
                double q = q2 + (double)ds / (n3 - n1) * ((n2 - n1 + ds) * (q3 - q2) / (n3 - n2) + (n3 - n2 - ds) * (q2 - q1) / (n2 - n1));
                q = q1 < q && q < q3 ? q :
                    ds == 1 ? q2 + (q3 - q2) / (n3 - n2) :
                    q2 - (q1 - q2) / (n1 - n2);
                n2 += ds;
                q2 = q;
            }
        }
        internal void Add(float s)
        {
            N++;
            if (N > 5)
            {
                if (s < q1) q1 = s;
                if (s < q2) n2++;
                if (s < q3) n3++;
                if (s < q4) n4++;
                if (s > q5) q5 = s;
                Adjust(0.25, 1, ref n2, n3, q1, ref q2, q3);
                Adjust(0.50, n2, ref n3, n4, q2, ref q3, q4);
                Adjust(0.75, n2, ref n4, N, q3, ref q4, q5);
            }
            else if (N == 5)
            {
                var a = new[] { q1, q2, q3, q4, s };
                Array.Sort(a);
                q1 = a[0];
                q2 = a[1];
                q3 = a[2];
                q4 = a[3];
                q5 = a[4];
            }
            else if (N == 4) q4 = s;
            else if (N == 3) q3 = s;
            else if (N == 2) q2 = s;
            else q1 = s;
        }
    }
}