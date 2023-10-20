namespace Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

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
    }
    public class Equity : Instrument
    {
        public IReadOnlyCollection<Exchange> Exchanges { get; }
        public Equity(string name, Country country, Currency currency, IReadOnlyCollection<Exchange> exchanges) : base(name, country, currency)
        {
            Exchanges = exchanges;
        }
    }
    public class Bond : Instrument
    {
        public IReadOnlyDictionary<DateTime, double> Coupons { get; }
        public Bond(string name, Country country, Currency currency, IReadOnlyDictionary<DateTime, double> coupons) : base(name, country, currency)
        {
            Coupons = coupons;
        }
    }
    public class Trade
    {
        public DateTime Date { get; }
        public int Quantity { get; }
        public double Cost { get; }
        public Trade(DateTime date, int quantity, double cost) { Date = date; Quantity = quantity; Cost = cost; }
    }
    public class Position
    {
        public Instrument Instrument { get; }
        public IReadOnlyList<Trade> Trades { get; }
        public double Price { get; }
        public Position(Instrument instrument, IReadOnlyList<Trade> trades, double price) { Instrument = instrument; Trades = trades; Price = price; }
        public int Quantity => Trades.Sum(i => i.Quantity);
        public double Cost => Trades.Sum(i => i.Cost);
        public double Profit => Price * Quantity - Cost;
    }
    public class Portfolio
    {
        public string Name { get; }
        public Currency Currency { get; }
        public IReadOnlyCollection<Position> Positions { get; }
        public Portfolio(string name, Currency currency, IReadOnlyCollection<Position> positions) { Name = name; Currency = currency; Positions = positions; }
        public double Profit(Func<Currency, double> fxRate) => Positions.Sum(i => i.Profit * fxRate(i.Instrument.Currency));
        public double[] RiskByPosition(Func<Currency, double> fxRate) => Positions.Select(i => i.Profit * fxRate(i.Instrument.Currency)).ToArray();
    }

    public static class ModelGen
    {
        public readonly static Gen<string> Name = Gen.String["ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 "];
        public readonly static Gen<Currency> Currency = Gen.Enum<Currency>();
        public readonly static Gen<Country> Country = Gen.Enum<Country>();
        public readonly static Gen<int> Quantity = Gen.Int[-99, 99].Select(Gen.Int[0, 5]).Select((m, e) => m * (int)Math.Pow(10, e));
        public readonly static Gen<double> Coupon = Gen.Int[0, 100].Select(i => 0.125 * i);
        public readonly static Gen<double> Price = Gen.Int[0001, 9999].Select(i => 0.01 * i);
        public readonly static Gen<DateTime> Date = Gen.Date[new DateTime(2000, 1, 1), new DateTime(2040, 1, 1)];
        public readonly static Gen<Equity> Equity = Gen.Select(Name, Country, Currency, Gen.Enum<Exchange>().HashSet[1, 3],
                                                    (n, co, cu, e) => new Equity(n, co, cu, e));
        public readonly static Gen<Bond> Bond = Gen.Select(Name, Country, Currency, Gen.SortedDictionary(Date, Coupon),
                                                    (n, co, cu, c) => new Bond(n, co, cu, c));
        public readonly static Gen<Instrument> Instrument = Gen.OneOf<Instrument>(Equity, Bond);
        public readonly static Gen<Trade> Trade = Gen.Select(Date, Quantity, Price, (dt, q, p) => new Trade(dt, q, q * p));
        public readonly static Gen<Position> Position = Gen.Select(Instrument, Trade.List, Price, (i, t, p) => new Position(i, t, p));
        public readonly static Gen<Portfolio> Portfolio = Gen.Select(Name, Currency, Position.Array, (n, c, p) => new Portfolio(n, c, p));
    }

    [Fact]
    public void Portfolio_Small_Mixed_Example()
    {
        var portfolio = ModelGen.Portfolio.Single(p =>
               p.Positions.Count == 5
            && p.Positions.Any(p => p.Instrument is Bond)
            && p.Positions.Any(p => p.Instrument is Equity)
        , "e2v0jI554Uya");
        var currencies = portfolio.Positions.Select(p => p.Instrument.Currency).Distinct().ToArray();
        var fxRates = ModelGen.Price.Array[currencies.Length].Single(a =>
            a.All(p => p is > 0.75 and < 1.5)
        , "ftXKwKhS6ec4");
        double fxRate(Currency c) => fxRates[Array.IndexOf(currencies, c)];
        Check.Hash(h =>
        {
            h.Add(portfolio.Positions.Select(p => p.Profit));
            h.Add(portfolio.Profit(fxRate));
            h.Add(portfolio.RiskByPosition(fxRate));
        }, 8262409355294024920, decimalPlaces: 2);
    }
}