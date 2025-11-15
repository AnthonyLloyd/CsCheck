namespace Tests;

using CsCheck;
using Xunit;

public class PerfTests(ITestOutputHelper output)
{
    [Fact]
    public void ValueTuple_Vs_Out()
    {
        static (double sum, double err) TwoSum_T(double a, double b)
        {
            var hi = a + b;
            var a2 = hi - b;
            return (hi, a2 - hi + b + (a - a2));
        }
        static double TwoSum_O(double a, double b, out double err)
        {
            var hi = a + b;
            var a2 = hi - b;
            err = a2 - hi + b + (a - a2);
            return hi;
        }
        Check.Faster(
            () => { var sum = TwoSum_O(2, 1e50, out var err); },
            () => { var (sum, err) = TwoSum_T(2, 1e50); }
        , repeat: 100, writeLine: output.WriteLine);
    }
    [Fact(Skip = "They are equal")]
    public void TryChecked_Vs_If()
    {
        static ulong TryChecked(ulong a, ulong b)
        {
            try
            {
                checked
                {
                    return a + b;
                }
            }
            catch
            {
                return ulong.MaxValue;
            }
        }
        static ulong If(ulong a, ulong b)
        {
            var x = a + b;
            return x >= a && x >= b ? x : ulong.MaxValue;
        }
        Gen.Select(Gen.Const(1UL), Gen.Const(2UL))
        .Faster(
            TryChecked,
            If
        , repeat: 100);
    }
    [Fact(Skip = "If is a lot faster")]
    public void TryChecked_Vs_If_Overflow()
    {
        static ulong TryChecked(ulong a, ulong b)
        {
            try
            {
                checked
                {
                    return a + b;
                }
            }
            catch
            {
                return ulong.MaxValue;
            }
        }
        static ulong If(ulong a, ulong b)
        {
            var x = a + b;
            return x >= a && x >= b ? x : ulong.MaxValue;
        }
        Check.Faster(
            () => If(ulong.MaxValue - 10, 20),
            () => TryChecked(ulong.MaxValue - 10, 20)
        , sigma: 200);
    }
}