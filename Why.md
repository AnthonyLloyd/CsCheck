# Why should you use random testing in C#?

## Property-based testing is a functional programming thing that comes from Haskell isn't it?

Well it did originate with the Haskell library QuickCheck, but there isn't anything particularly functional programming about random testing.

We should also drop the term property-based. Developers often get stuck trying to think what 'property' their code has.

It's better to think of it as a more automated and powerful way of doing example based testing.
When coming up with small number of simple examples consider if the test could be written for any or a range of examples.

The advantage is the test will be run for 100 or more examples with varying size.
The developer isn't directly coming up with the examples so they are a bit more of an independent test.
Random testing will come up with quirky examples e.g. empty sets, or things that sum to zero.
Also large examples tend to be more [efficient](https://youtu.be/1LNEWF8s1hI?t=2055) at catching bugs. 

Random tests are able to make a stronger claim than a test with a few examples.
In fact if the test can generate any example and run for a long time it's close to being a proof.
A 'long time' could be 60 seconds since the examples are run in parallel by default and often millions can be run in this time.

When a random test finds a bug it will shrink it down to the smallest possible example so you can more easily repeat and diagnose the problem.
CsCheck is particularly good at this.

## Gen

Gen.Double.Array.List
Gen.Double[0, 100].Array[1, 30].List

## No Brainers

Serialization
Caches and collections
Calculations and algorithms
Code refactoring
Multithreading and concurrency