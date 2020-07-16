using System;
using Xunit;
using CsCheck;
using System.Linq;

namespace Tests
{
    public class ModelTests
    {
        public enum Currency { EUR, GBP, USD, CAD };
        public enum Country { DE, GB, US, CA };
        public class Instrument { public string Name; public Country Country; public Currency Currency; };
        public class Equity : Instrument { };
        public class Bond : Instrument { public double Coupon; };
        public class Trade { public Instrument Instrument; public DateTime Date; public int Quantity; public double Cost; };
        public class Position { public Instrument Instrument; public int Quantity; public double Cost; public double Price; };
        public class Portfolio { public string Name; public Currency Currency; public Position[] Positions; };

        public static class MyGen
        {
            public static Gen<Currency> Currency = Gen.OneOf(Enum.GetValues(typeof(Currency)).Cast<Currency>().ToArray());
        }

    }
}