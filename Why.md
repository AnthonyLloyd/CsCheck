# Why should you use a random testing library in C#?

## Property-based testing is a functional programming thing that comes from Haskell isn't it?

Well it did originate with the Haskell library QuickCheck, but there isn't anything particularly functional about random testing.

We should also drop the term property-based. Developers often get stuck trying to think what 'property' their code has.
Random testing is lot more than validating a property. The most powerful random tests are model-based followed by metamorphic.
We should be thinking of those first.

It's better to think of it as a more automated and powerful way of doing example based testing.
When coming up with a small number of simple examples consider if the test could be written for a range of examples instead.

The advantage is the test will be run for 100 or more examples with varying size.
The developer isn't directly coming up with the examples so they are more of an independent test.
Random testing will come up with quirky examples e.g. empty collections, or things that sum to zero.
Also large examples tend to be more [efficient at catching bugs](https://youtu.be/1LNEWF8s1hI?t=2055). 

Random tests are able to make a stronger claim than a test with a few examples.
In fact a test that generates any example and runs for a long time is close to being a proof.
A 'long time' could be 60 seconds in CsCheck since examples are run in parallel by default and often millions can be run in this time.

When a random test finds a bug it will shrink it down to the smallest possible example so you can more easily reproduce and diagnose the problem.
CsCheck is particularly good at this as it's the only library that can always shrink to the simplest example and reproduce it directly.

## Gen It

Instead of coming up with examples we need to create a generator `Gen<T>` for them.
This may sound like a pain, but actually it's really simple with the composable fluent `Gen` classes in CsCheck, and can be done in one or two lines of code.
The generators created for domain types can be composed and reused across a number of tests e.g. serialization and domain logic.

We start with a highly defaulted generator `Gen.Double.Array.List` say but may want to be more specific `Gen.Double[0.0, 100.0].Array[5].List[1, 10]` about the range of values. 

Some testing libraries can create the generator for you automatically using reflection but this can lead to a number of bugs for the library author and a lack of control and insight for the library user.
Fluent style composition similar to LINQ is a much more robust and extensible option.

## Some No Brainers

- Serialization - the number of bugs seen in serialization code (looking at you json) is almost criminal given how easy it is to roundtrip test serialization using random testing.
- Caches and collections - often a key part of server and client side code these can be tested against a suitable simplified test model with `Model Based` testing.
- Calculations and algorithms - often possible to generalize examples for calculations and algorithms and check the result given the input. Algorithm often have properties they must guarantee. Rounding error issues automatically tested.
- Code refactoring - keep a copy of the original code with the test, refactor for simplicity and performance, safe in the knowledge it still produces the same results. Pair with a `Faster` test to monitor the relative performance over a range of inputs. Or if a copy is not feasible create a `Regression` test to comprehensively make sure there is no change.
- Multithreading - test on the same object instance across multiple threads and examples. Shrink even works for `Parallel` testing.

## Raise Your Game

CsCheck has been used to help test serialization in Microsoft Orleans and Hagar, and high performance immutable collections in ImTools.

It has given me the ability to write safe native code and interop, high performance collections, faster and simpler servers code and much more.

I noticed that it increased the quality of solutions I can attempt. It basically raised my game.