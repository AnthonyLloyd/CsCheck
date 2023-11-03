namespace Tests;

using System;
using System.Diagnostics;

public static class MathX
{
    public static (double hi, double lo) TwoSum(double a, double b)
    {
        var hi = a + b;
        var a2 = hi - b;
        return (hi, a2 - hi + b + (a - a2));
    }

    public static (double hi, double lo) FastTwoSum(double a, double b)
    {
        //Debug.Assert(Math.Abs(a) >= Math.Abs(b));
        var hi = a + b;
        return (hi, a - hi + b);
    }

    public static (double hi, double lo) TwoSub(double a, double b)
    {
        var hi = a - b;
        var b2 = a - hi;
        var a2 = hi + b2;
        return (hi, a - a2 + (b2 - b));
    }

    public static (double hi, double lo) TwoMul(double a, double b)
    {
        var prod = a * b;
        return (prod, Math.FusedMultiplyAdd(a, b, -prod));
    }

    /// <summary>Kahan summation</summary>
    public static double KSum(this double[] values)
    {
        var sum = 0.0;
        var c = 0.0;
        foreach (var value in values)
            (sum, c) = FastTwoSum(sum, value + c);
        return sum;
    }

    /// <summary>Neumaier summation</summary>
    public static double NSum(this double[] values)
    {
        var sum = 0.0;
        var c = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            (sum, var ci) = TwoSum(sum, values[i]);
            c += ci;
        }
        return sum + c;
    }

    /// <summary>Shewchuk summation</summary>
    public static double FSum(this double[] values)
    {
        Span<double> partials = stackalloc double[16];
        int count = 0;
        var hi = 0.0;
        var lo = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            (var v, lo) = TwoSum(values[i], lo);
            int c = 0;
            for (int j = 0; j < count; j++)
            {
                (v, var partial) = TwoSum(v, partials[j]);
                if (partial != 0.0)
                    partials[c++] = partial;
            }
            (hi, v) = TwoSum(hi, v);
            if (v != 0.0)
            {
                if (c == partials.Length)
                {
                    var newPartials = new double[partials.Length * 2];
                    partials.CopyTo(newPartials);
                    partials = newPartials;
                }
                partials[c++] = v;
            }
            count = c;
        }

        if (count == 0)
            return lo + hi;

        Span<double> x = [lo, ..partials[..count], hi];
        Compress(ref x);
        var sum = 0.0;
        foreach (var v in x)
            sum += v;
        return sum;

        //for (int i = 0; i < count; i++)
        //    lo += partials[i];
        //return lo + hi;
    }

    static void Compress(ref Span<double> e)
    {
        var Q = e[^1];
        var bottom = e.Length - 1;
        for (int i = e.Length - 2; i >= 0; i--)
        {
            (Q, var q) = FastTwoSum(Q, e[i]);
            if (q != 0.0)
            {
                e[bottom--] = Q;
                Q = q;
            }
        }
        e[bottom] = Q;
        var top = 0;
        for (int i = bottom + 1; i < e.Length; i++)
        {
            (Q, var q) = FastTwoSum(e[i], Q);
            if (q != 0.0)
            {
                e[top++] = q;
            }
        }
        e[top] = Q;
        e = e[..(top + 1)];
    }

    public static double SSum(this double[] values)
    {
        if (values.Length == 0)
            return 0.0;
        values = (double[])values.Clone();
        Array.Sort(values, (x, y) => Math.Abs(x).CompareTo(Math.Abs(y)));
        var prev = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            var next = values[i];
            if (next == -prev)
            {
                values[i - 1] = 0;
                values[i] = 0;
                prev = 0;
            }
            else
            {
                prev = next;
            }
        }
        return values.FSum();
    }

    public static long Mantissa(double d, out int exponent)
    {
        var bits = BitConverter.DoubleToInt64Bits(d);
        exponent = (int)((bits >> 52) & 0x7FFL);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFF;
        if (exponent == 0)
        {
            exponent = -1074;
        }
        else
        {
            exponent -= 1075;
            mantissa |= 1L << 52;
        }
        return (bits & (1L << 63)) == 0 ? mantissa : -mantissa;
    }
}