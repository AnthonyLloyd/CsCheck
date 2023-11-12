namespace Tests;

using CsCheck;
using System;
using System.Linq;
using Xunit;

public class FloatingPointTests(Xunit.Abstractions.ITestOutputHelper output)
{
    readonly static Gen<long> genPriceLong = Gen.Long[1, 1_000_000_000_000];
    static double PriceLongToDouble(long priceLong) => (double)priceLong / 1_000_000;
    static decimal PriceLongToDecimal(long priceLong) => (decimal)priceLong / 1_000_000;


    static Gen<(long PriceLong, long Quantity)> GenPriceQuantity(int priceMaxValue, int priceDecimalPlaces, long lineValueMaxValue)
    {
        var priceFactor = (long)Math.Pow(10, priceDecimalPlaces);
        var genPriceLong = Gen.Long[1, priceMaxValue * priceFactor];
        return genPriceLong.SelectMany(p =>
        {
            var quantityMax = lineValueMaxValue * priceFactor / p;
            return Gen.Long[-quantityMax, quantityMax].Select(q => (p, q));
        });
    }

    static string PriceLongToString(long priceLong, int dp = 6)
    {
        var s = priceLong.ToString();
        if (s.Length < dp + 1)
        {
            return string.Concat("0.", s.PadLeft(dp, '0'));
        }
        var span = s.AsSpan();
        var beforeDot = span[..^dp];
        var afterDot = span.Slice(span.Length - dp, dp).TrimEnd('0');
        return afterDot.Length == 0 ? new string(beforeDot) : string.Concat(beforeDot, ".", afterDot);
    }

    [Fact]
    public void PriceDoubleToString()
    {
        genPriceLong.Sample(
              priceLong => PriceLongToDouble(priceLong).ToString() == PriceLongToString(priceLong)
            , output.WriteLine);
    }

    [Fact]
    public void PriceDecimalToString()
    {
        genPriceLong.Sample(
              priceLong => PriceLongToDecimal(priceLong).ToString("0.######") == PriceLongToString(priceLong)
            , output.WriteLine);
    }

    [Fact]
    public void SumQuantityTimesPriceDoubleToString()
    {
        const int dp = 2;
        var priceFactor = (long)Math.Pow(10, dp);
        GenPriceQuantity(1_000_000, dp, 100_000_000_000)
        .Array[2, 10_000]
        .Sample(a =>
        {
            var valueLong = a.Sum(i => i.PriceLong * i.Quantity);
            var valueDouble = a.Select(i => (double)i.PriceLong / priceFactor * i.Quantity).NSum();
            var doubleString = Math.Round(valueDouble, dp).ToString();
            var longString = PriceLongToString(valueLong, dp);
            return doubleString == longString;
        });
    }

    [Fact]
    public void SumQuantityTimesPriceDecimalToString()
    {
        const int dp = 2;
        var priceFactor = (long)Math.Pow(10, dp);
        GenPriceQuantity(1_000_000, dp, 100_000_000_000)
        .Array[2, 100_000]
        .Sample(a =>
        {
            var valueLong = a.Sum(i => i.PriceLong * i.Quantity);
            var valueDecimal = a.Select(i => (decimal)i.PriceLong / priceFactor * i.Quantity).Sum();
            var decimalString = valueDecimal.ToString("0.##");
            var longString = PriceLongToString(valueLong, dp);
            return decimalString == longString;
        });
    }
}