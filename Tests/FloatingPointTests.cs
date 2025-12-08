namespace Tests;

using CsCheck;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

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
