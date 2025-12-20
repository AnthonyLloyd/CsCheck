namespace Tests;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

public interface ICache<K, V>
{
    void Set(K key, V value);
    bool TryGetValue(K key, [MaybeNullWhen(false)] out V value);
    int Count { get; }
    IEnumerable<K> Keys { get; }
}

public class SieveLruCache<K, V>(int capacity) : ICache<K, V> where K : notnull
{
    class Node(K key, V value)
    {
        public Node Next = null!;
        public readonly K Key = key;
        public V Value = value;
        public bool Visited;
    }

    private readonly Dictionary<K, Node> _dictionary = [];
    private readonly ReaderWriterLockSlim _lock = new();
    private Node head = null!, hand = null!;

    private void Evict()
    {
        var prev = hand;
        var node = prev.Next;
        while (node.Visited)
        {
            node.Visited = false;
            prev = node;
            node = node.Next;
        }
        prev.Next = node.Next;
        hand = prev;
        if (head == node)
            head = prev;
        _dictionary.Remove(node.Key);
    }

    private void AddToHead(Node node)
    {
        var count = _dictionary.Count;
        if (count > 2)
        {
            if (count > capacity) Evict();
            node.Next = head.Next;
            head.Next = node;
        }
        else if (count == 2)
        {
            node.Next = head;
            head.Next = node;
        }
        if (head == hand)
            hand = node;
        head = node;
    }

    public bool TryGetValue(K key, [MaybeNullWhen(false)] out V value)
    {
        _lock.EnterReadLock();
        try
        {
            if (_dictionary.TryGetValue(key, out var node))
            {
                node.Visited = true;
                value = node.Value;
                return true;
            }
            value = default;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Set(K key, V value)
    {
        _lock.EnterWriteLock();
        try
        {
            ref var node = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, key, out _);
            if (node is null)
            {
                node = new Node(key, value);
                AddToHead(node);
            }
            else
            {
                node.Value = value;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            var count = _dictionary.Count;
            _lock.ExitReadLock();
            return count;
        }
    }

    public IEnumerable<K> Keys
    {
        get
        {
            _lock.EnterReadLock();
            var keys = _dictionary.Keys.ToArray();
            _lock.ExitReadLock();
            return keys;
        }
    }
}