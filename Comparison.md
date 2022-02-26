# Comparison with other random testing libraries

See the [shrinking challenge](https://github.com/jlink/shrinking-challenge) for a set of example shrinking problems across testing libraries.

## Arbitrary

Arb is basically the generator and the shrinker. You only have this when the shrinker is not integrated (automatic).
So [FsCheck](https://github.com/fscheck/FsCheck) has Arb but [Hedgehog](https://github.com/hedgehogqa) doesn't.
No downside to not have it apart from you need an algorithm to automatically shrink.

The problem with Arb and shrinkers in general is that they don't compose. Composing types and Gens is easy.
But you have to manually write the code to shrink a composed type.
People don't write this code and that is why [FsCheck](https://github.com/fscheck/FsCheck) is not good at shrinking composed types.

## Integrated shrinking

Now the shrinking algorithms. [Hedgehog](https://github.com/hedgehogqa) creates not just a value but a tree of values (lazy) and you can compose trees.
CsCheck creates a value and Size tuple. The Size is a comparison proxy for the value (it's an int64 tree). This also composes.

Shrinking for [Hedgehog](https://github.com/hedgehogqa) is simply exploring the tree and testing each one.
Shrinking for CsCheck is a process of generating a new value, checking its size is less than the current failing case and if so testing.
You can think of it more as a Monte-Carlo shrinker than a path explorer shrinker.

There must be many pros and cons between them but there are a few reasons the Monte-Carlo approach is better.

Firstly in the composed tree way you explore along axes to shrink and don't cover the whole space.
Obviously you can't cover a very large space completely but even in a small space this axis exploration can miss some obvious shrinking.
If you look at the Hedgehog [Version example](https://github.com/hedgehogqa/fsharp-hedgehog/blob/master/doc/tutorial.md#-integrated-shrinking-is-an-important-quality-of-hedgehog)
it can't shrink if failures only happened when two or three numbers are equal. CsCheck is the only random testing library that can shrink for cases like this.

```fsharp
let version =
    Range.constantBounded ()
    |> Gen.byte
    |> Gen.map int
    |> Gen.tuple3
    |> Gen.map (fun (ma, mi, bu) -> Version (ma, mi, bu))

Property.print <| property {
    let! v = version
    return not(v.Major = v.Minor && v.Minor = v.Build)
    }

>
*** Failed! Falsifiable (after 16 tests):
249.249.249
```


```csharp
[Fact]
public void Version_Same()
{
    Gen.Select(Gen.Byte, Gen.Byte, Gen.Byte)
    .Select(t => new Version(t.V0, t.V1, t.V2))
    .Sample(v =>
    {
        if(v.Major == v.Minor && v.Minor == v.Build)
        {
            writeLine("Fail: " + v.ToString());
            return false;
        }
        return true;
    }, size: 100_000_000);
}

>
Failed Tests.CheckTests.Version_Same [5 s]
Error Message:
 CsCheck.CsCheckException : CsCheck_Seed = "2dyl_qlOCdjb" (4 shrinks, 29,604,901 skipped, 100,000,000 total)
Stack Trace:
   at CsCheck.Check.Sample[T](Gen`1 gen, Func`2 predicate, String seed, Int32 size, Int32 threads) in C:\Users\Ant\src\CsCheck\CsCheck\Check.cs:line 198
   at Tests.CheckTests.Version_Same() in C:\Users\Ant\src\CsCheck\Tests\CheckTests.cs:line 299
Standard Output Messages:
 Fail: 155.155.155
 Fail: 36.36.36
 Fail: 22.22.22
 Fail: 3.3.3
 Fail: 0.0.0
```

The [shrinking challenge test](https://github.com/jlink/shrinking-challenge/blob/main/challenges/binheap.md) where CsCheck managed to shrink to a new smaller case is another example.
Most other libraries are more faithful to the original QuickCheck design and they all stop at a similar larger example.

Size is also a better representation of comparison especially for collections or a number of axes.
There are examples where increasing on one axis while decreasing on others can lead to smaller cases e.g. if Version fails for `2 * ma + mi + bu ≥ 255 * 2`
CsCheck will be able to shrink to `255.0.0` but [Hedgehog](https://github.com/hedgehogqa) won't.

For concurrency testing random shrinkers also has an advantage. Concurrency tests may not fail deterministically.
This is a real problem for path explorer shrinkers. The only solution is to repeat each test multiple times (10 for QuickCheck) since they need to follow defined paths.
For a random shrinker you can just continue testing different random cases until one fails and limit the size to that each time.

For CsCheck it has to generate and check size in a loop. This has to be as fast as possible to be able to quickly create smaller values.
This is why CsCheck uses a fast random generator ([PCG](https://www.pcg-random.org)) and a good Size algorithm. It can shrink more complex spaces.
It has the advantage over the tree way in that we know the seed for the shrunk case. It means you can continue the shrinking later after a CI failure.
It is much better at shrinking more complex types, you just have to leave it shrinking for a short time.