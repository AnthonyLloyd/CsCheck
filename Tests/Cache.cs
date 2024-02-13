namespace Tests;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

public interface ICache<K, V>
{
    void Set(K key, V value);
    bool TryGetValue(K key, [MaybeNullWhen(false)] out V value);
    int Count { get; }
    IEnumerable<K> Keys { get; }
}

public static class CacheExtensions
{
    static class TaskCompletionSources<K, V>
    {
        public static ConcurrentDictionary<(ICache<K, V>, K), TaskCompletionSource<V>> Current = [];
    }

    public static async ValueTask<V> GetOrAddAtomicAsync<K, V>(this ICache<K, V> cache, K key, Func<K, Task<V>> factory) where K : notnull
    {
        if (cache.TryGetValue(key, out var value))
            return value;
        var myTcs = new TaskCompletionSource<V>();
        var tcs = TaskCompletionSources<K, V>.Current.GetOrAdd((cache, key), myTcs);
        if (tcs != myTcs) return await tcs.Task;
        try
        {
            if (!cache.TryGetValue(key, out value))
                cache.Set(key, value = await factory(key));
            myTcs.SetResult(value);
            return value;
        }
        catch (Exception ex)
        {
            myTcs.SetException(ex);
            throw;
        }
        finally
        {
            TaskCompletionSources<K, V>.Current.TryRemove((cache, key), out _);
        }
    }
}