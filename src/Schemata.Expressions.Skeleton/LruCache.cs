using System;
using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Stores a bounded set of values and evicts the least recently used entry when capacity is exceeded.
/// </summary>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int                                     _capacity;
    private readonly object                                  _gate = new();
    private readonly LinkedList<Entry>                       _list = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;

    /// <summary>
    ///     Creates a cache with the supplied entry capacity and key comparer.
    /// </summary>
    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null) {
        if (capacity <= 0) {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _map      = new(comparer);
    }

    /// <summary>
    ///     Gets a cached value or creates and stores one for the key.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TValue> factory) {
        if (factory is null) {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (_gate) {
            if (_map.TryGetValue(key, out var found)) {
                _list.Remove(found);
                _list.AddFirst(found);
                return found.Value.Value;
            }
        }

        var value = factory();

        lock (_gate) {
            if (_map.TryGetValue(key, out var existing)) {
                // Lost the race; another thread inserted while we were compiling.
                // Return the winning value so the cache stays single-instance per key.
                _list.Remove(existing);
                _list.AddFirst(existing);
                return existing.Value.Value;
            }

            var node = new LinkedListNode<Entry>(new(key, value));
            _list.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity) {
                var last = _list.Last!;
                _list.RemoveLast();
                _map.Remove(last.Value.Key);
            }

            return value;
        }
    }

    /// <summary>
    ///     Attempts to get a cached value and promotes the entry when found.
    /// </summary>
    public bool TryGet(TKey key, out TValue value) {
        lock (_gate) {
            if (_map.TryGetValue(key, out var node)) {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    #region Nested type: Entry

    private sealed class Entry
    {
        public Entry(TKey key, TValue value) {
            Key   = key;
            Value = value;
        }

        public TKey Key { get; }

        public TValue Value { get; }
    }

    #endregion
}
