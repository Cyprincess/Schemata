using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Stores parsed expression trees, LINQ expression trees, and compiled delegates for expression languages.
/// </summary>
public static class ExpressionCache
{
    private static readonly LruCache<ExpressionCacheKey, IExpressionTree>  Trees       = new(500);
    private static readonly LruCache<ExpressionCacheKey, LambdaExpression> Expressions = new(500);

    private static readonly LruCache<LambdaExpression, Delegate> Delegates = new(200, LambdaReferenceComparer.Instance);

    /// <summary>
    ///     Gets a parsed expression tree from the cache or creates and stores it.
    /// </summary>
    public static IExpressionTree GetOrAddTree(ExpressionCacheKey key, Func<IExpressionTree> factory) {
        return Trees.GetOrAdd(key, factory);
    }

    /// <summary>
    ///     Gets a LINQ expression tree from the cache or creates and stores it.
    /// </summary>
    public static Expression<Func<TContext, TResult>> GetOrAddExpression<TContext, TResult>(
        ExpressionCacheKey                        key,
        Func<Expression<Func<TContext, TResult>>> factory
    ) {
        return (Expression<Func<TContext, TResult>>)Expressions.GetOrAdd(key, factory);
    }

    /// <summary>
    ///     Gets a compiled delegate for a LINQ expression tree or compiles and stores it.
    /// </summary>
    public static Func<TContext, TResult> GetOrAddDelegate<TContext, TResult>(
        Expression<Func<TContext, TResult>> expression
    ) {
        if (expression is null) {
            throw new ArgumentNullException(nameof(expression));
        }

        return (Func<TContext, TResult>)Delegates.GetOrAdd(expression, expression.Compile);
    }

    #region Nested type: LambdaReferenceComparer

    private sealed class LambdaReferenceComparer : IEqualityComparer<LambdaExpression>
    {
        public static readonly LambdaReferenceComparer Instance = new();

        #region IEqualityComparer<LambdaExpression> Members

        public bool Equals(LambdaExpression? x, LambdaExpression? y) { return ReferenceEquals(x, y); }

        public int GetHashCode(LambdaExpression obj) { return RuntimeHelpers.GetHashCode(obj); }

        #endregion
    }

    #endregion

    #region Nested type: LruCache<TKey, TValue>

    private sealed class LruCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly int                                     _capacity;
        private readonly object                                  _gate = new();
        private readonly LinkedList<Entry>                       _list = [];
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

    #endregion
}
