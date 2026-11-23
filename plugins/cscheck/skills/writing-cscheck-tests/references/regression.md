# Regression tests: Single and Check.Hash

A regression test pins today's results, so a later change that moves them fails.

## Single

`Single` pins one generated example. `gen.Single(predicate)` always throws: with `Single <Type> seed = "..."` when it finds an example the predicate accepts, with `Single exception with seed = "..."` when the generator or the predicate throws, or with `Single not found after 1000000 tries` (`CsCheck_SingleLimit`). Paste the seed in as the last argument, `gen.Single(predicate, "<seed>")`, and from then on it returns what that seed generates, the same example while the generator and CsCheck don't change. If either changes so the seed no longer gives an example the predicate accepts, it throws `predicate no longer satisfied`.

## Check.Hash

`Check.Hash` checks a hash of a calculation's results:

```csharp
Check.Hash(h =>
{
    h.Add("positions");
    h.Add(portfolio.Positions.Select(p => p.Profit));
    h.Add("total");
    h.Add(portfolio.Profit(fxRate));
}, 5857230471108592669, decimalPlaces: 2);
```

- To get the expected value for a new test, leave it out: it throws `Hash is <N>`. Paste N in.
- A mismatch says `Actual X but expected Y`. A machine that has run the test passing keeps a cache of the values it hashed, under `CsCheck` in the local application data folder (`%LOCALAPPDATA%` on Windows, `~/.local/share` on Linux, `~/Library/Application Support` on macOS). There a mismatch stops at the first value that differs: `Actual 'v' but Expected 'w'. (last string was 'label')`. So add string labels, such as `h.Add("total")`, as breadcrumbs. A fresh CI machine has no cache, so it reports only the two hashes.
- The action runs once on a pass, and up to four times on a mismatch or with no expected value, so keep it pure.
- `decimalPlaces:` and `significantFigures:` round doubles, floats and decimals before hashing, so rounding noise between platforms doesn't move the hash. The expected value carries an offset chosen away from rounding boundaries.
- `Hash.Add` takes `bool`, `char`, the integer types up to 64 bits, `double`, `float`, `decimal`, `string`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, and an `IEnumerable<T>` of any of them. Cast an enum to its integer type, add a `DateOnly` as its `DayNumber`, and add a record or class field by field.
