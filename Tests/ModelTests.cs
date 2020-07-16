using System;
using Xunit;
using CsCheck;
using System.Collections.Generic;

namespace Tests
{
    public class ModelTests
    {
        public enum Currency { EUR, GBP, USD, CAD };
        public enum Country { DE, GB, US, CA };
        public enum Exchange { LMAX, EQTA, GLPS, XCNQ }
        public class Instrument { public string Name; public Country Country; public Currency Currency; };
        public class Equity : Instrument { public HashSet<Exchange> Exchanges; };
        public class Bond : Instrument { public Dictionary<DateTime, double> Coupons; };
        public class Trade { public Instrument Instrument; public DateTime Date; public int Quantity; public double Cost; };
        public class Position { public Instrument Instrument; public int Quantity; public double Cost; public double Price; };
        public class Portfolio { public string Name; public Currency Currency; public Position[] Positions; };

        public static class MyGen
        {
            public static Gen<string> Name = Gen.String["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "];
            public static Gen<Currency> Currency = Gen.Enum<Currency>();
            public static Gen<Country> Country = Gen.Enum<Country>();
            public static Gen<Equity> Equity = Gen.Select(Name, Country, Currency, Gen.Enum<Exchange>().HashSet[1, 3],
                            (n, co, cu, e) => new Equity { Name = n, Country = co, Currency = cu, Exchanges = e });
            public static Gen<Bond> Bond = Gen.Select(Name, Country, Currency, Gen.Dictionary(Gen.DateTime, Gen.Double),
                            (n, co, cu, c) => new Bond { Name = n, Country = co, Currency = cu, Coupons = c });
            public static Gen<Instrument> Instrument = Gen.Frequency((2, Equity.Cast<Instrument>()), (1, Bond.Cast<Instrument>()));
            public static Gen<Trade> Trade = Gen.Select(Instrument, Gen.DateTime, Gen.Int, Gen.Double,
                            (i, dt, q, c) => new Trade { Instrument = i, Date = dt, Quantity = q, Cost = c });
            public static Gen<Position> Position = Gen.Select(Instrument, Gen.Int, Gen.Double, Gen.Double,
                            (i, q, c, p) => new Position { Instrument = i, Quantity = q, Cost = c, Price = p });
            public static Gen<Portfolio> Portfolio = Gen.Select(Name, Currency, Position.Array,
                            (n, c, p) => new Portfolio { Name = n, Currency = c, Positions = p });
        }

        [Fact]
        public void Model()
        {
            MyGen.Portfolio.Sample(p => { });
        }
    }
}