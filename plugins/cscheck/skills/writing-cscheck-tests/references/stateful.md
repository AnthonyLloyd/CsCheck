# Stateful tests: model-based, metamorphic and parallel

Each applies random operations to a generated starting state and shrinks a failure to a simpler start and fewer, simpler operations (simpler arguments, for `SampleMetamorphic`'s one pair).

`SampleMetamorphic` generates the start again for its second route, and `SampleParallel` for each order it tries, so the generator must build a new object each time, as `Gen.Const(() => new ...())` does. From `Gen.Const(instance)` they compare one object with itself, and only an exception fails them.

Name every operation, `Gen.Int.Operation<A, M>(i => $"Add({i})", ...)`, or a failure prints it as `Op0 <args>`.

## SampleModelBased

Generate a pair of the real type and a simple model of it, such as a `HashSet` for a custom set (here `SetSlim`, from CsCheck's Tests project), apply the same random operations to both, and check they end up equal.

```csharp
Gen.Int.Array.Select(a => (new SetSlim<int>(a), new HashSet<int>(a)))
.SampleModelBased(
    Gen.Int.Operation<SetSlim<int>, HashSet<int>>(i => $"Add({i})",
        (ss, i) => ss.Add(i),
        (hs, i) => hs.Add(i)));
```

- Each run applies 0 to 127 operations and then compares the two once. The operations' return values are thrown away, so check a return value inside the operation and throw if it's wrong.
- The comparison is `Check.Equal`. Two sequences are compared element by element, in order, unless either is a `Dictionary`, `HashSet`, `ConcurrentDictionary`, `ConcurrentBag`, `ImmutableDictionary`, `ImmutableHashSet`, `ReadOnlyDictionary` or `ReadOnlySet`, or their elements are `KeyValuePair`s and they aren't both lists or arrays: then order is ignored, and duplicates count. That's why a custom set compares with a `HashSet` model as above, and a custom map with any dictionary. CsCheck 5.0.1 matches these types exactly, so it compares a `FrozenSet`, or a subclass of one of them, in order: pass `equal: Check.EqualUnordered` for those. Anything that isn't a sequence falls back to `Equals`, so pass `equal:` for a real type that isn't one, or `equal: Check.EqualUnordered` for an unordered collection compared with an ordered one.
- To check the interesting cases were reached, not just the easy one, pass `classify:` over the model state with `writeLine:` set. That writes a table of how often each operation ran, split by the state each acted on, with rows named `Op0`, `Op1` by argument position even when the operations have names. `classify:` does nothing without `writeLine:`.
- A failure prints the operations, the initial actual and model, and the final actual and model, or the exception.
- For async code, use `SampleModelBasedAsync`, whose generator gives a `Task<(Actual, Model)>`, with `Gen.Operation<Actual, Model>` and async lambdas.

## SampleMetamorphic

Do the same thing two different ways from one starting sample, and check both give the same result. It suits code with no simple model, where two routes lead to one answer. `MapSlim` here is a custom map from CsCheck's Tests project.

```csharp
Gen.Dictionary(Gen.Int, Gen.Byte)
.Select(d => new MapSlim<int, byte>(d))
.SampleMetamorphic(
    Gen.Select(Gen.Int[0, 100], Gen.Byte, Gen.Int[0, 100], Gen.Byte).Metamorphic<MapSlim<int, byte>>(
        (d, t) => { d[t.Item1] = t.Item2; d[t.Item3] = t.Item4; },
        (d, t) => { if (t.Item1 == t.Item3) d[t.Item3] = t.Item4; else { d[t.Item3] = t.Item4; d[t.Item1] = t.Item2; } }));
```

- It takes one `Metamorphic` pair, and generates the starting sample twice from the same seed, one for each route.
- A failure prints the initial state, the operations, and both final states.

## SampleParallel

Run random operations on one instance from several threads, then check the result matches running them one at a time in some order (linearizability).

```csharp
Gen.Const(() => new ConcurrentQueue<int>())
.SampleParallel(
    Gen.Int.Operation<ConcurrentQueue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i)),
    Gen.Operation<ConcurrentQueue<int>>("TryDequeue()", q => q.TryDequeue(out _)));
```

- It can also check against a model, which doesn't need to be thread-safe, from a pair:
  ```csharp
  Gen.Const(() => (new ConcurrentQueue<int>(), new Queue<int>()))
  .SampleParallel(
      Gen.Int.Operation<ConcurrentQueue<int>, Queue<int>>(i => $"Enqueue({i})", (q, i) => q.Enqueue(i), (q, i) => q.Enqueue(i)),
      Gen.Operation<ConcurrentQueue<int>, Queue<int>>("TryDequeue()", q => q.TryDequeue(out _), q => q.TryDequeue(out _)));
  ```
- Each run does 0 to 10 operations one at a time (`maxSequentialOperations`), then 2 to 5 in parallel (`maxParallelOperations`) on 2 or more threads, up to `threads`. The runs themselves go one at a time.
- Its operations must be synchronous: an async lambda is a compile error, so wait inside a synchronous operation instead.
- Only the final states are compared, not the operations' return values, with `Check.Equal` as in `SampleModelBased`. An order that throws counts as not matching.
- A failure prints the initial state, the operations run one at a time and in parallel, the thread each ran on, the final state, and every order tried with its result.
- To reproduce one, pass the printed seed as `seed:`; a replay that passes is retried up to `replay` times (default 100). Appending the printed thread list, as in `seed: "<seed>[0,1,1]"`, also pins the thread each operation runs on.
