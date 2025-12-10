namespace Tests;

using CsCheck;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class CacheTests
{
    class ConcurrentDictionaryCache<K, V> : ConcurrentDictionary<K, V>, ICache<K, V> where K : notnull
    {
        IEnumerable<K> ICache<K, V>.Keys => Keys;
        public void Set(K key, V value) => this[key] = value;
    }

    [Test]
    public void GetOrAddAtomicAsync_SampleParallel()
    {
        Check.SampleParallel(
            Gen.Const(() => new ConcurrentDictionaryCache<int, int>()),
            Gen.Int[1, 5].Operation<ConcurrentDictionaryCache<int, int>>((d, i) => d.GetOrAddAtomicAsync(i, i => Task.FromResult(i)).AsTask()),
            equal: (a, b) => Check.Equal(a.Keys, b.Keys),
            print: a => Check.Print(a.Keys)
        );
    }

    [Test]
    public async Task GetOrAddAtomicAsync_Exception()
    {
        var cache = new ConcurrentDictionaryCache<int, int>();
        await Assert.ThrowsAsync<CsCheckException>(() => cache.GetOrAddAtomicAsync(1, _ => Task.Run(int () => throw new CsCheckException("no1"))).AsTask())
            .WithMessage("no1");
    }

    [Test]
    public async Task GetOrAddAtomicAsync_ExceptionSync()
    {
        var cache = new ConcurrentDictionaryCache<int, int>();
        await Assert.ThrowsAsync<CsCheckException>(() => cache.GetOrAddAtomicAsync(1, _ => throw new CsCheckException("no2")).AsTask())
            .WithMessage("no2");
    }
}