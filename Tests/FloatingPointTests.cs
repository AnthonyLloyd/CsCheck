namespace Tests;

using CsCheck;
using System;
using System.Linq;
using Xunit;

public class FloatingPointTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
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
        }, writeLine: output.WriteLine);
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

    [Fact]
    public void DoubleSumPrecision12()
    {
        DoubleSumPrecision(12, 350); // 9_999_999_999.99
    }

    [Fact]
    public void DoubleSumPrecision11()
    {
        DoubleSumPrecision(11, 1_500); // 999_999_999.99
    }

    [Fact]
    public void DoubleSumPrecision10()
    {
        DoubleSumPrecision(10, 7_100); // 99_999_999.99
    }

    [Fact]
    public void DoubleSumPrecision9()
    {
        DoubleSumPrecision(9, 35_500); // 9_999_999.99
    }

    [Fact]
    public void DoubleVsDecimal_Faster()
    {
        Check.Faster(new DoubleAdd(), new DecimalAdd(), threads: 1, repeat: 1000, writeLine: output.WriteLine, timeout: 300);
    }

    public struct DoubleAdd() : IInvoke
    {
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
}

// 1. Limitations of the options
// 2. Performance
// 3. Implementation
// 4. Scaling
// 5. Allocation and FSum