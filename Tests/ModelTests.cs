﻿using System;
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
        public class Instrument
        {
            public string Name { get; }
            public Country Country { get; }
            public Currency Currency { get; }
            public Instrument(string name, Country country, Currency currency) { Name = name; Country = country; Currency = currency; }
        };
        public class Equity : Instrument
        {
            public IReadOnlyCollection<Exchange> Exchanges { get; }
            public Equity(string name, Country country, Currency currency, IReadOnlyCollection<Exchange> exchanges) : base(name, country, currency)
            {
                Exchanges = exchanges;
            }
        };
        public class Bond : Instrument
        {
            public IReadOnlyDictionary<DateTime, double> Coupons { get; }
            public Bond(string name, Country country, Currency currency, IReadOnlyDictionary<DateTime, double> coupons) : base(name, country, currency)
            {
                Coupons = coupons;
            }
        };
        public class Trade
        {
            public DateTime Date { get; }
            public int Quantity { get; }
            public double Cost { get; }
            public Trade(DateTime date, int quantity, double cost) { Date = date; Quantity = quantity; Cost = cost; }
        };
        public class Position
        {
            public Instrument Instrument { get; }
            public IReadOnlyList<Trade> Trades { get; }
            public double Price { get; }
            public Position(Instrument instrument, IReadOnlyList<Trade> trades, double price) { Instrument = instrument; Trades = trades; Price = price; }
            public int Quantity => Trades.Sum(i => i.Quantity);
            public double Cost => Trades.Sum(i => i.Cost);
            public double Profit => Price * Quantity - Cost;
        };
        public class Portfolio
        {
            public string Name { get; }
            public Currency Currency { get; }
            public IReadOnlyCollection<Position> Positions { get; }
            public Portfolio(string name, Currency currency, IReadOnlyCollection<Position> positions) { Name = name; Currency = currency; Positions = positions; }
        };

        public static class MyGen
        {
            public static Gen<string> Name = Gen.String["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "];
            public static Gen<Currency> Currency = Gen.Enum<Currency>();
            public static Gen<Country> Country = Gen.Enum<Country>();
            public static Gen<int> Quantity = Gen.Int[-99, 99].Select(Gen.Int[0, 5]).Select(t => t.V0 * (int)Math.Pow(10, t.V1));
            public static Gen<double> Coupon = Gen.Int[0, 100].Select(i => i * 0.125);
            public static Gen<double> Price = Gen.Int[0001, 9999].Select(i => i * 0.01);
            public static Gen<DateTime> Date = Gen.DateTime[new DateTime(2000, 1, 1), DateTime.Today].Select(i => i.Date);
            public static Gen<Equity> Equity = Gen.Select(Name, Country, Currency, Gen.Enum<Exchange>().HashSet[1, 3], (n, co, cu, e) => new Equity(n, co, cu, e));
            public static Gen<Bond> Bond = Gen.Select(Name, Country, Currency, Gen.SortedDictionary(Date, Coupon), (n, co, cu, c) => new Bond(n, co, cu, c));
            public static Gen<Instrument> Instrument = Gen.Frequency((2, Equity.Cast<Instrument>()), (1, Bond.Cast<Instrument>()));
            public static Gen<Trade> Trade = Gen.Select(Date, Quantity, Price, (dt, q, p) => new Trade(dt, q, q * p));
            public static Gen<Position> Position = Gen.Select(Instrument, Trade.List, Price, (i, t, p) => new Position(i, t, p));
            public static Gen<Portfolio> Portfolio = Gen.Select(Name, Currency, Position.Array, (n, c, p) => new Portfolio(n, c, p));
        }

        [Fact]
        public void Model()
        {
            MyGen.Portfolio.Sample(p => { });
        }
    }
}