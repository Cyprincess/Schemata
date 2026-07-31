using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace System.Linq;

/// <summary>
///     Extension methods for <see cref="IAsyncEnumerable{T}" /> providing projection,
///     materialization, and dictionary conversion.
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    ///     Projects each element of an async sequence using the specified mapping function.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <typeparam name="T">The source element type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="mapper">A mapping function applied to each element.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of projected elements.</returns>
    public static async IAsyncEnumerable<TResult> Map<TResult, T>(
        this IAsyncEnumerable<T>?                  source,
        Func<T, TResult>                           mapper,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        if (source is null) {
            yield break;
        }

        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            yield return mapper(item);
        }
    }

#if !NET10_0_OR_GREATER
    /// <summary>
    ///     Materializes an async enumerable into a <see cref="List{T}" />.
    ///     Conditional on <c>!NET10_0_OR_GREATER</c> because .NET 10 ships
    ///     a built-in <c>ToListAsync</c>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list containing all elements from the source.</returns>
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source, CancellationToken ct = default) {
        var result = new List<T>();

        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            result.Add(item);
        }

        return result;
    }
#endif

#if !NET10_0_OR_GREATER
    /// <summary>
    ///     Creates a dictionary from an async enumerable using the specified key selector.
    ///     Conditional on <c>!NET10_0_OR_GREATER</c> because .NET 10 ships
    ///     a built-in <c>ToDictionaryAsync</c>.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="keySelector">A key selector function.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A dictionary keyed by the selected key with elements as values.</returns>
    public static Task<Dictionary<TKey, TSource>> ToDictionaryAsync<TSource, TKey>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TKey>            keySelector,
        CancellationToken              ct = default
    )
        where TKey : notnull {
        return source.ToDictionaryAsync(keySelector, null, ct);
    }

    /// <summary>
    ///     Creates a dictionary from an async enumerable using the specified key selector and
    ///     comparer.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="keySelector">A key selector function.</param>
    /// <param name="comparer">An optional equality comparer for keys.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A dictionary keyed by the selected key with elements as values.</returns>
    public static Task<Dictionary<TKey, TSource>> ToDictionaryAsync<TSource, TKey>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TKey>            keySelector,
        IEqualityComparer<TKey>?       comparer,
        CancellationToken              ct = default
    )
        where TKey : notnull {
        return source.ToDictionaryAsync(keySelector, e => e, comparer, ct);
    }

    /// <summary>
    ///     Creates a dictionary from an async enumerable using the specified key and element
    ///     selectors.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TElement">The value type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="keySelector">A key selector function.</param>
    /// <param name="elementSelector">An element selector function.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A dictionary with selected keys and values.</returns>
    public static Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TSource, TKey, TElement>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TKey>            keySelector,
        Func<TSource, TElement>        elementSelector,
        CancellationToken              ct = default
    )
        where TKey : notnull {
        return source.ToDictionaryAsync(keySelector, elementSelector, null, ct);
    }

    /// <summary>
    ///     Creates a dictionary from an async enumerable using the specified key selector,
    ///     element selector, and comparer.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TElement">The value type.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="keySelector">A key selector function.</param>
    /// <param name="elementSelector">An element selector function.</param>
    /// <param name="comparer">An optional equality comparer for keys.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A dictionary with selected keys and values.</returns>
    public static async Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TSource, TKey, TElement>(
        this IAsyncEnumerable<TSource> source,
        Func<TSource, TKey>            keySelector,
        Func<TSource, TElement>        elementSelector,
        IEqualityComparer<TKey>?       comparer,
        CancellationToken              ct = default
    )
        where TKey : notnull {
        var result = new Dictionary<TKey, TElement>(comparer);

        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            result.Add(keySelector(item), elementSelector(item));
        }

        return result;
    }
#endif
}
