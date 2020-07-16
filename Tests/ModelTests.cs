﻿using System;
using Xunit;
using CsCheck;

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
            public static Gen<Currency> Currency = Gen.Enum<Currency>();
            public static Gen<Country> Country = Gen.Enum<Country>();
            public static Gen<Equity> Equity = Gen.Select(Gen.String, Country, Currency,
                            (n, co, cu) => new Equity { Name = n, Country = co, Currency = cu });
            public static Gen<Bond> Bond = Gen.Select(Gen.String, Country, Currency, Gen.Double,
                            (n, co, cu, c) => new Bond { Name = n, Country = co, Currency = cu, Coupon =c });
            public static Gen<Instrument> Instrument = Gen.Frequency((2, Equity.Cast<Instrument>()), (1, Bond.Cast<Instrument>()));
            public static Gen<Trade> Trade = Gen.Select(Instrument, Gen.DateTime, Gen.Int, Gen.Double,
                            (i, dt, q, c) => new Trade { Instrument = i, Date = dt, Quantity = q, Cost = c });
            public static Gen<Position> Position = Gen.Select(Instrument, Gen.Int, Gen.Double, Gen.Double,
                            (i, q, c, p) => new Position { Instrument = i, Quantity = q, Cost = c, Price = p });
            public static Gen<Portfolio> Portfolio = Gen.Select(Gen.String, Currency, Position.Array,
                            (n, c, p) => new Portfolio { Name = n, Currency = c, Positions = p});
        }

        [Fact]
        public void Model()
        {
            MyGen.Portfolio.Sample(p => { });
        }
    }
}