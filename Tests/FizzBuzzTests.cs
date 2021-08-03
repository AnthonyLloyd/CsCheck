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
            Gen.Int.Uniform.Sample(i => FizzBuzz(i) switch
            {
                "FizzBuzz" => FizzBuzz(i + 15).Equals("FizzBuzz"),
                "Fizz" => FizzBuzz(i + 3).StartsWith("Fizz"),
                "Buzz" => FizzBuzz(i + 5).EndsWith("Buzz"),
                var fb => int.Parse(fb) == i && !FizzBuzz(i + 3).StartsWith("Fizz") && !FizzBuzz(i + 5).EndsWith("Buzz")
            });
        }
    }
}