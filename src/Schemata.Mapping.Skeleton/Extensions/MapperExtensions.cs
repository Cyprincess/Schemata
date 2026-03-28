using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace Schemata.Mapping.Skeleton;

/// <summary>
///     Extension methods for mapping collections using <see cref="ISimpleMapper" />.
/// </summary>
public static class MapperExtensions
{
    /// <summary>
    ///     Maps each element in the source collection to type <typeparamref name="T" />, skipping null results.
    /// </summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="mapper">The mapper.</param>
    /// <param name="source">The source collection.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An enumerable of mapped objects.</returns>
    public static IEnumerable<T> Each<T>(
        this ISimpleMapper  mapper,
        IEnumerable<object> source,
        CancellationToken   ct = default
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();

            var result = mapper.Map<T>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }

    /// <summary>
    ///     Maps each element in the strongly-typed source collection to <typeparamref name="TDestination" />, skipping null
    ///     results.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TDestination">The destination element type.</typeparam>
    /// <param name="mapper">The mapper.</param>
    /// <param name="source">The source collection.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An enumerable of mapped objects.</returns>
    public static IEnumerable<TDestination> Each<TSource, TDestination>(
        this ISimpleMapper   mapper,
        IEnumerable<TSource> source,
        CancellationToken    ct = default
    ) {
        foreach (var item in source) {
            ct.ThrowIfCancellationRequested();

            var result = mapper.Map<TSource, TDestination>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }

    /// <summary>
    ///     Asynchronously maps each element in the source to type <typeparamref name="T" />, skipping null results.
    /// </summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="mapper">The mapper.</param>
    /// <param name="source">The async source collection.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of mapped objects.</returns>
    public static async IAsyncEnumerable<T> EachAsync<T>(
        this ISimpleMapper                         mapper,
        IAsyncEnumerable<object>                   source,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            var result = mapper.Map<T>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }

    /// <summary>
    ///     Asynchronously maps each element in the strongly-typed source to <typeparamref name="TDestination" />, skipping
    ///     null results.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TDestination">The destination element type.</typeparam>
    /// <param name="mapper">The mapper.</param>
    /// <param name="source">The async source collection.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of mapped objects.</returns>
    public static async IAsyncEnumerable<TDestination> EachAsync<TSource, TDestination>(
        this ISimpleMapper                         mapper,
        IAsyncEnumerable<TSource>                  source,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        await foreach (var item in source.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            var result = mapper.Map<TSource, TDestination>(item);
            if (result is null) {
                continue;
            }

            yield return result;
        }
    }
}
