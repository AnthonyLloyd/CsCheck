namespace Tests;

using CsCheck;

public class FizzBuzzTests
{
    public static string FizzBuzz(int i) =>
          i % 15 == 0 ? "FizzBuzz"
        : i % 3 == 0 ? "Fizz"
        : i % 5 == 0 ? "Buzz"
        : i.ToString();

    [Test]
    public async Task Induction_Initial()
    {
        await Assert.That(FizzBuzz(0)).IsEqualTo("FizzBuzz");
        await Assert.That(FizzBuzz(1)).IsEqualTo("1");
        await Assert.That(FizzBuzz(2)).IsEqualTo("2");
        await Assert.That(FizzBuzz(3)).IsEqualTo("Fizz");
        await Assert.That(FizzBuzz(4)).IsEqualTo("4");
        await Assert.That(FizzBuzz(5)).IsEqualTo("Buzz");
    }

    [Test]
    public void Induction_Random()
    {
        Gen.Int[0, 1_000_000].Sample(i => FizzBuzz(i) switch
        {
            "FizzBuzz" => FizzBuzz(i + 15).Equals("FizzBuzz"),
            "Fizz" => FizzBuzz(i + 3).StartsWith("Fizz"),
            "Buzz" => FizzBuzz(i + 5).EndsWith("Buzz"),
            var fb => int.Parse(fb) == i && !FizzBuzz(i + 3).StartsWith("Fizz") && !FizzBuzz(i + 5).EndsWith("Buzz")
        });
    }
}