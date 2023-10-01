namespace Tests;

using CsCheck;
using System;
using System.Collections.Generic;
using Xunit;

public class MantissaTests
{
    [Fact]
    public void Mantissa()
    {
        Gen.Double[-1e20, 1e20]
        .Sample(d =>
        {
            var m1 = MathMantissa.MantissaAndExponent(d, out var e1);
            var d1 = Math.ScaleB(m1, e1);
            return d == d1;
        });
    }
}

public static class MathMantissa
{
    public static long MantissaAndExponent(double d, out int exponent)
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

    public static double LSum(IEnumerable<double> values)
    {
        var totalMantissa = 0L;
        var totalExponent = 0;
        foreach(var v in values)
        {
            var mantissa = MantissaAndExponent(v, out var exponent);
            if(totalExponent > exponent)
            {
                totalMantissa <<= totalExponent - exponent;
                totalExponent = exponent;
            }
            else
            {
                mantissa <<= exponent - totalExponent;
            }
            totalMantissa += mantissa;
        }
        return Math.ScaleB(totalMantissa, totalExponent);
    }
}