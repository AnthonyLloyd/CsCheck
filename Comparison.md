# Comparison with other random testing libraries

## Arbitrary

Arb is basically the generator and the shrinker. You only have this when the shrinker is not integrated (automatic). So FsCheck has Arb but Hedgehog doesn't. No downside to not have it apart from you need an algorithm to automatically shrink, more later.

My problem with Arb and shrinkers in general is that they don't compose. Composing types and Gens is easy. But you have to manually write the code to shrink a composed type. People don't write this code and that is why FsCheck is not so great at shrinking composed types.

## Shrinking

Now the shrinking algorithm. Hedgehog creates not just a value but a tree of values (lazy) and you can compose trees. CsCheck creates a value and Size tuple. The Size is a comparison proxy for the value (it's an int64 tree). This also composes.

Shrinking for Hedgehog is simply exploring the tree and testing each one. Shrinking for CsCheck is a process of generating a new value, checking its size is less than the current failing case and if so testing. You can think of it more as a Monte-Carlo shrinker than a path explorer shrinker.

There must be many pros and cons between them but there are a few reasons the Monte-Carlo approach is better.

Firstly in the composed tree way you explore along axis to shrink and don't cover the whole space. Obviously you can't cover a very large space completely but even in a small space this axis exploration can miss some obvious shrinking. If you look at the Hedgehog [Version example](https://github.com/hedgehogqa/fsharp-hedgehog/blob/master/doc/tutorial.md#-integrated-shrinking-is-an-important-quality-of-hedgehog) it can't shrink if failures only happened when two or three numbers are equal. Size is also a better representation of if one value is smaller than another e.g. for collection values.

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

For CsCheck it has to generate and check size in a loop. This has to be as quick as possible to be able to quickly create smaller values. This is why CsCheck uses a fast random generator (PCG) and good Size algorithm. It can shrink more complex spaces. It has the advantage over the tree way in that we know the seed for the shrunk case. It means you can repeat the shrinking later on your laptop after a CI failure. I find it much better at shrinking more complex types, you just have to leave it shrinking for 5 mins.

One outstanding issue for the Monte-Carlo way is that for very rare failures it can take a long time to find the next value and failure. It doesn't cut down the total space well (tree way cuts it too much). This can be worked around by once you know some dimensions of the failure you can limit the test to these and continue.