# Getting started with CsCheck

CsCheck helps you test rules, not just examples. You describe something your code should always do, and CsCheck generates many inputs to try to break that rule. When it finds a failure, it tries to simplify the input into a small, useful reproduction.

## Why use CsCheck alongside normal unit tests?

A normal unit test checks an example you chose. For a reversible encoder, that might be:

```csharp
var original = "Hello world";

var encoded = Encode(original);
var decoded = Decode(encoded);

Assert.Equal(original, decoded);
```

This is a useful test: it proves the code works for `"Hello world"`, and it documents an important expected outcome. But it does not explore empty strings, unusual Unicode characters, or other values you did not think to write down.

With CsCheck, the same rule can be tested with generated values:

```csharp
Gen.String.Sample(original =>
{
    var encoded = Encode(original);
    var decoded = Decode(encoded);

    return decoded == original;
});
```

Instead of choosing every test value yourself, you state what should always be true and let CsCheck try many values. This is commonly called *property-based testing*, but the important idea is simply automatic exploration of inputs. CsCheck does not replace normal unit tests: keep important named examples, and add CsCheck tests where trying many inputs gives you extra confidence.

## A tiny first test

Install the package in your test project:

```powershell
dotnet add package CsCheck
```

Then write a test using the test framework you already use. This complete TUnit example checks a small deterministic rule with a real CsCheck generator:

```csharp
using CsCheck;
using TUnit.Core;

public class AdditionTests
{
    [Test]
    public void Adding_zero_does_not_change_an_integer()
    {
        Gen.Int.Sample(value => value + 0 == value);
    }
}
```

`Sample` asks `Gen.Int` for generated integers and runs the predicate for each one. Returning `false` or throwing an exception means the rule failed. The default is 100 iterations; `iter:` and `time:` can change that. The same body works in xUnit, NUnit, MSTest, or a plain test method; the surrounding attribute and assertion style are yours.

Here is another complete example that uses a `Guid` round trip without manually calculating expected values:

```csharp
using CsCheck;
using TUnit.Core;

public class GuidTests
{
    [Test]
    public void A_guid_can_be_parsed_after_formatting()
    {
        Gen.Guid.Sample(original =>
            Guid.TryParse(original.ToString(), out var parsed) && parsed == original);
    }
}
```

## Generators create test values

A `Gen<T>` is something that knows how to create many test values of type `T`. Start with the built-in generators:

```csharp
Gen.Int           // integers
Gen.String        // strings
Gen.Guid          // GUIDs
Gen.Int[0, 100]   // integers from 0 through 100
```

You can combine generators to make values from your own domain. For example, this creates a generator for an `OrderLine` from a product name and a quantity:

```csharp
var orderLines = Gen.Select(Gen.String, Gen.Int[1, 100],
    (product, quantity) => new OrderLine(product, quantity));
```

The generator describes which values are sensible for the test. Start simple; you can make it more specific as you learn what the rule needs.

## Shrinking makes failures easier to use

The first failing value might be complicated:

```text
"AAABF😃98173819273891273..."
```

Yet the bug might only need:

```text
"😃"
```

**Shrinking** means simplifying a failing input again and again while checking that the failure still happens. CsCheck does not only try to find a failing input; it also tries to turn the failure into the smallest useful reproduction it can find. Failures report a seed, which you can pass back through `seed:` to reproduce that generated case and continue investigating it.

## When should I use CsCheck?

CsCheck is especially helpful when the input space is too large or awkward to enumerate by hand, including serialization and deserialization round trips; parsers and formatters; calculations and algorithms; collections, caches, and data structures; validation and conversion code; stateful APIs; refactoring; optimized replacements; boundary conditions; and concurrent code where race conditions may hide.

A normal example-based test can be clearer when the example is valuable documentation itself, for example, a test named around `CalculateOntarioTax(100m)` and a known business outcome. Keep those examples. Add CsCheck when exploring many generated inputs tests the same rule more thoroughly.

## The main testing styles

| Term | Plain-English meaning |
| --- | --- |
| Generator / `Gen<T>` | Create lots of test values of type `T`. |
| Random testing | Try many automatically generated inputs. |
| Property-based testing | Describe a rule that should always be true. |
| Shrinking | Reduce a failing input to a simpler failure. |
| Model-based testing | Compare your implementation with a simpler, trusted implementation. |
| Metamorphic testing | Do something two equivalent ways and compare the results. |
| Parallel testing | Run operations concurrently to uncover race-condition bugs. |
| Regression testing | Check that behaviour has not unexpectedly changed. |
| Performance testing | Compare performance over many different inputs. |
| Causal profiling | Experimentally find which concurrent code regions limit performance. |

### Model-based testing: compare against a reference

Suppose you have a specialized `FastDictionary<TKey, TValue>` and want confidence that it behaves like `Dictionary<TKey, TValue>`. Use the simpler, trusted `Dictionary` as a reference model. CsCheck can generate a sequence such as:

```text
Add(12)
Remove(5)
Add(7)
Clear()
Add(99)
Remove(7)
```

It performs each generated sequence on both collections and checks they stay equal after every operation. If a long sequence fails, shrinking can reduce it to a short sequence such as `Add(4)`, `Remove(4)`, `Add(4)`. In other words: use a simple trusted implementation as the reference model for a more complicated implementation.

CsCheck supports this directly with `SampleModelBased`; see the [SetSlim example in the README](../README.md#model-based-testing) for the real API.

### Metamorphic testing: get the same result two ways

Sometimes there is no simple reference model, but you know two different routes should reach the same answer. A round trip is the classic case: `Decode(Encode(x))` should equal `x`, checkable without ever knowing the encoded form. That is *metamorphic* testing: doing the same work two equivalent ways and comparing.

A closely related option during a refactoring or optimization is to keep both versions and compare them directly:

```csharp
Gen.Int.Sample(input => OldCalculate(input) == NewCalculate(input));
```

This is useful when refactoring, replacing an algorithm, introducing SIMD, native, or other high-performance code, or rewriting an implementation without manually calculating the answer for every input. CsCheck also has `SampleMetamorphic` for operations that should produce the same result when performed in different equivalent orders; see the [MapSlim example in the README](../README.md#metamorphic-testing).

### Specification testing: check the rules you were given

The two styles above need something to compare against: a reference implementation, or a second version of your own code. Sometimes you have neither, and what you have instead is a document: a protocol, an exchange's rules, a regulation. The rules are written down, but nothing checks that your code follows them.

`Spec` lets you write those rules as named requirements over a small state machine, each carrying the sentence it came from:

```csharp
.Invariant("NO-OVER-REFUND", "Never refund more than was paid.", o => o.Refunded <= o.Paid)
.Never("NO-SHIP-UNPAID", "Goods only leave once the money has arrived.",
    (before, after) => after.Status is Status.Shipped or Status.Delivered && after.Paid == 0)
```

Then it does something the other styles cannot: rather than sampling, it enumerates *every* reachable state and checks every requirement on every step. When that finishes, the requirements are proved for the model rather than tested. It will also inject deliberate defects to show your requirements are strong enough to catch them, and drive your real code down the same steps to check it agrees.

This costs more thought than the other styles, because the state machine has to be small enough to enumerate. Start with [Tests/Specs/SpecIntroTests.cs](../Tests/Specs/SpecIntroTests.cs), an order lifecycle with seven states, small enough to check the answer by hand, and then [docs/Spec.md](Spec.md) for the full guide.

## Getting started

1. Install CsCheck in your existing test project.
2. Pick a small deterministic function.
3. State one rule that should always hold, such as “formatting and then parsing a value returns the original value.”
4. Choose a simple generator such as `Gen.Int`, `Gen.String`, or `Gen.Guid`.
5. Call `Sample` with a predicate that returns `true` when the rule holds.
6. Run the test as usual.
7. If it fails, inspect the reduced input and reported seed, then add a named example test if that failure deserves permanent documentation.

## A useful mental model

A normal unit test says:

> Here are some situations where I know my code works.

A CsCheck test says:

> Here is the rule my code is supposed to obey. Try to prove me wrong.

Think of it as giving a QA developer the ability to generate many inputs automatically, asking them to keep trying to break a function, and then asking them to reduce any failure to a small reproduction.
