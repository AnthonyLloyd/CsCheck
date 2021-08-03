using CsCheck;
using Xunit;

namespace Tests
{
    public class FizzBuzzTests
    {
        public static string FizzBuzz(int i) =>
              i % 15 == 0 ? "FizzBuzz"
            : i % 3 == 0 ? "Fizz"
            : i % 5 == 0 ? "Buzz"
            : i.ToString();

        [Fact]
        public void Induction_Initial()
        {
            Assert.Equal("FizzBuzz", FizzBuzz(0));
            Assert.Equal("1", FizzBuzz(1));
            Assert.Equal("2", FizzBuzz(2));
            Assert.Equal("Fizz", FizzBuzz(3));
            Assert.Equal("4", FizzBuzz(4));
            Assert.Equal("Buzz", FizzBuzz(5));
        }

        [Fact]
        public void Induction_Random()
        {
            Gen.Int.Sample(i => FizzBuzz(i) switch
            {
                "FizzBuzz" => "FizzBuzz".Equals(FizzBuzz(i + 15)),
                "Fizz" => FizzBuzz(i + 3).Contains("Fizz"),
                "Buzz" => FizzBuzz(i + 5).Contains("Buzz"),
                var fb => i == int.Parse(fb) && !FizzBuzz(i + 3).Contains("Fizz") && !FizzBuzz(i + 5).Contains("Buzz")
            });
        }
    }
}