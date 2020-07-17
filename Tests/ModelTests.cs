using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

namespace Tests
{
    public class ModelTests
    {
        public enum Currency { EUR, GBP, USD, CAD };
        public enum Country { DE, GB, US, CA };
        public enum Exchange { LMAX, EQTA, GLPS, XCNQ }
        public class Instrument { public string Name; public Country Country; public Currency Currency; };
        public class Equity : Instrument { public ISet<Exchange> Exchanges; };
        public class Bond : Instrument { public SortedDictionary<DateTime, double> Coupons; };
        public class Trade { public DateTime Date; public int Quantity; public double Cost; };
        public class Position
        {
            public Instrument Instrument; public List<Trade> Trades; public double Price;
            public int Quantity => Trades.Sum(i => i.Quantity);
            public double Cost => Trades.Sum(i => i.Cost);
            public double Profit => Price * Quantity - Cost;
        };
        public class Portfolio { public string Name; public Currency Currency; public Position[] Positions; };

        public static class MyGen
        {
            public static Gen<string> Name = Gen.String["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "];
            public static Gen<Currency> Currency = Gen.Enum<Currency>();
            public static Gen<Country> Country = Gen.Enum<Country>();
            public static Gen<int> Quantity = Gen.Int[-99, 99].Select(Gen.Int[0, 5]).Select(t => t.V0 * (int)Math.Pow(10, t.V1));
            public static Gen<double> Coupon = Gen.Int[0, 100].Select(i => i * 0.125);
            public static Gen<double> Price = Gen.Int[0001, 9999].Select(i => i * 0.01);
            public static Gen<DateTime> Date = Gen.DateTime[new DateTime(2000, 1, 1), DateTime.Today].Select(i => i.Date);
            public static Gen<Equity> Equity = Gen.Select(Name, Country, Currency, Gen.Enum<Exchange>().HashSet[1, 3],
                            (n, co, cu, e) => new Equity { Name = n, Country = co, Currency = cu, Exchanges = e });
            public static Gen<Bond> Bond = Gen.Select(Name, Country, Currency, Gen.SortedDictionary(Date, Coupon),
                            (n, co, cu, c) => new Bond { Name = n, Country = co, Currency = cu, Coupons = c });
            public static Gen<Instrument> Instrument = Gen.Frequency((2, Equity.Cast<Instrument>()), (1, Bond.Cast<Instrument>()));
            public static Gen<Trade> Trade = Gen.Select(Date, Quantity, Price,
                            (dt, q, p) => new Trade { Date = dt, Quantity = q, Cost = q * p });
            public static Gen<Position> Position = Gen.Select(Instrument, Trade.List, Price,
                            (i, t, p) => new Position { Instrument = i, Trades = t, Price = p });
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