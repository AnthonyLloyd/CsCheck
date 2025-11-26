namespace Tests;

using CsCheck;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

public class FloatingPointTests()
{
    [Test]
    public void DoubleSupportsRoundtripPrecisionTo15()
    {
        var genDigit = Gen.Char['0', '9'];
        var genPriceIO = Gen.Int[1, 15]
            .SelectMany(p =>
                genDigit.Array[p + 1].Select(Gen.Int[0, p], (cs, d) =>
                {
                    cs[d] = '.';
                    return new string(cs);
                }));
        genPriceIO.Sample(priceString =>
        {
            var priceDouble = double.Parse(priceString);
            return priceString.Trim('0').TrimEnd('.') == priceDouble.ToString("#.#################");
        }, writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void X()
    {
        var quantity = 1000;
        var price = 99.99;
        var fxRate = 1.2345;
        var localValue = MathX.TwoMul(quantity, price, out double lo1);
        var usdValue = MathX.TwoMul(localValue, fxRate, out double lo2);
    }

    private static void DoubleSumPrecision(int significantFigures, int maxLength)
    {
        const double scaling = 0.01;
        var lower = (long)Math.Pow(10, significantFigures - 1);
        var upper = (long)Math.Pow(10, significantFigures) - 1;
        Gen.Long[lower, upper].Array[100, maxLength]
        .Sample(longs =>
        {
            var longSum = longs.Sum();
            var doubleSum = longs.Sum(i => i * scaling);
            return (doubleSum / scaling).ToString("#") == longSum.ToString();
        });
    }

    private static void DoubleNSumPrecision(int significantFigures, int maxLength)
    {
        const double scaling = 0.01;
        var lower = (long)Math.Pow(10, significantFigures - 1);
        var upper = (long)Math.Pow(10, significantFigures) - 1;
        Gen.Long[lower, upper].Array[100, maxLength]
        .Sample(longs =>
        {
            var longSum = longs.Sum();
            var doubleSum = longs.Select(i => i * scaling).ToArray().NSum();
            return (doubleSum / scaling).ToString("#") == longSum.ToString();
        });
    }

    [Test]
    public void DoubleSumPrecision12()
    {
        DoubleSumPrecision(12, 350); // 1_000_000_000.00 - 9_999_999_999.99
    }

    [Test]
    public void DoubleNSumPrecision12()
    {
        DoubleNSumPrecision(12, 1_700); // 1_000_000_000.00 - 9_999_999_999.99
    }

    [Test]
    public void DoubleSumPrecision11()
    {
        DoubleSumPrecision(11, 1_500); // 100_000_000.00 - 999_999_999.99
    }

    [Test]
    public void DoubleNSumPrecision11()
    {
        DoubleNSumPrecision(11, 17_900); // 100_000_000.00 - 999_999_999.99
    }

    [Test]
    public void DoubleSumPrecision10()
    {
        DoubleSumPrecision(10, 7_100); // 10_000_000.00 - 99_999_999.99
    }

    [Test]
    public void DoubleNSumPrecision10()
    {
        DoubleNSumPrecision(10, 181_000); // 10_000_000.00 - 99_999_999.99
    }

    [Test]
    public void DoubleSumPrecision9()
    {
        DoubleSumPrecision(9, 35_500); // 1_000_000.00 - 9_999_999.99
    }

    [Test]
    public void DoubleNSumPrecision9()
    {
        DoubleNSumPrecision(9, 1_816_000); // 1_000_000.00 - 9_999_999.99
    }

    [Test]
    public void DoubleVsDecimalAdd_Faster()
    {
        Check.Faster(new DoubleAdd(), new DecimalAdd(), threads: 1, repeat: 100, writeLine: TUnitX.WriteLine);
    }

    [Test]
    public void DoubleVsDecimalMul_Faster()
    {
        Check.Faster(new DoubleMul(), new DecimalMul(), threads: 1, repeat: 100, writeLine: TUnitX.WriteLine);
    }

#pragma warning disable RCS1118 // Mark local variable as const.
#pragma warning disable IDE0059 // Unnecessary assignment of a value

    public struct DoubleAdd() : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public readonly void Invoke()
        {
            var l1 = 12345.6789;
            var l2 = 1234.56778;
            l1 = l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
        }
    }

    public struct DecimalAdd() : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public readonly void Invoke()
        {
            var l1 = 12345.6789M;
            var l2 = 1234.56778M;
            l1 = l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
               + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
        }
    }

    public struct DoubleMul() : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public readonly void Invoke()
        {
            var l1 = 1.03456789;
            var l2 = 1.056778;
            l1 = l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2;
        }
    }

    public struct DecimalMul() : IInvoke
    {
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public readonly void Invoke()
        {
            var l1 = 1.03456789M;
            var l2 = 1.056778M;
            l1 = l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
               * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2;
        }
    }
}

public class BenchmarkTests()
{
    //[Test]
    public void FloatingAdd_Perf()
    {
        var logger = new AccumulationLogger();
        BenchmarkRunner.Run<FloatingAddBenchmarks>(DefaultConfig.Instance.AddLogger(logger));
        Console.WriteLine(logger.GetLog());
    }

    //[Test]
    public void FloatingMul_Perf()
    {
        var logger = new AccumulationLogger();
        BenchmarkRunner.Run<FloatingMulBenchmarks>(DefaultConfig.Instance.AddLogger(logger));
        Console.WriteLine(logger.GetLog());
    }

    //| Method     | Mean       | Error   | StdDev   | Median     | Ratio | RatioSD |
    //|----------- |-----------:|--------:|---------:|-----------:|------:|--------:|
    //| DoubleAdd  |   249.3 ns | 0.09 ns |  0.50 ns |   249.2 ns |  1.00 |    0.00 |
    //| DecimalAdd | 5,501.9 ns | 4.94 ns | 29.79 ns | 5,504.4 ns | 22.06 |    0.15 |

    //| Method     | Mean        | Error    | StdDev   | Ratio | RatioSD |
    //|----------- |------------:|---------:|---------:|------:|--------:|
    //| DoubleMul  |    496.9 ns |  1.04 ns |  0.98 ns |  1.00 |    0.00 |
    //| DecimalMul | 17,392.7 ns | 52.59 ns | 43.92 ns | 34.99 |    0.13 |
}

public class FloatingAddBenchmarks
{
    decimal m1;
    double d1;
    decimal m2;
    double d2;

    [GlobalSetup]
    public void Setup()
    {
        d1 = 12345.6789;
        d2 = 1234.56778;
        m1 = 12345.6789M;
        m2 = 1234.56778M;
    }

    [Benchmark(Baseline = true)]
    public double DoubleAdd()
    {
        var l1 = d1;
        var l2 = d2;
        return l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
    }

    [Benchmark]
    public decimal DecimalAdd()
    {
        var l1 = m1;
        var l2 = m2;
        return l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2
             + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2 + l1 + l2;
    }
}

public class FloatingMulBenchmarks
{
    decimal m1;
    double d1;
    decimal m2;
    double d2;

    [GlobalSetup]
    public void Setup()
    {
        d1 = 1.03456789;
        d2 = 1.056778;
        m1 = 1.03456789M;
        m2 = 1.056778M;
    }

    [Benchmark(Baseline = true)]
    public double DoubleMul()
    {
        var l1 = d1;
        var l2 = d2;
        return l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2;
    }

    [Benchmark]
    public decimal DecimalMul()
    {
        var l1 = m1;
        var l2 = m2;
        return l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2
             * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2 * l1 * l2;
    }
}
// 1. Limitations of the options
// 2. Performance
// 3. Implementation
// 4. Scaling
// 10_000_000_000 x 0.01 USD
// Realise on no primitive obsession and well defined io and service boundary.
// 5. Allocation and KSum