using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Streams a backend result superset through a local residual predicate, collecting one page after
///     a local skip under a bounded scan. The scan stops early when an exact total is not required and
///     the page plus its look-ahead are filled; it fails rather than materialize an unbounded superset.
/// </summary>
public static class ResidualPage
{
    /// <summary>
    ///     Scans <paramref name="source" /> applying <paramref name="residual" />, returning the page
    ///     of <paramref name="pageSize" /> rows after skipping <paramref name="skip" /> matches,
    ///     whether a further match follows, and the exact residual-passing total when
    ///     <paramref name="countExact" /> is set.
    /// </summary>
    /// <typeparam name="T">The streamed row type.</typeparam>
    /// <param name="source">The backend superset to filter locally.</param>
    /// <param name="residual">The residual predicate applied to each row.</param>
    /// <param name="skip">The number of matching rows to skip before the page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cap">The maximum number of source rows scanned before failing.</param>
    /// <param name="countExact">Whether to scan the whole source for an exact matching total.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The page, whether more matches follow, and the exact total when requested.</returns>
    /// <exception cref="InvalidOperationException">The scan reaches <paramref name="cap" /> rows before the result is known.</exception>
    public static async Task<(List<T> Page, bool HasMore, int? Total)> ScanAsync<T>(
        IAsyncEnumerable<T> source,
        Func<T, bool>       residual,
        int                 skip,
        int                 pageSize,
        int                 cap,
        bool                countExact,
        CancellationToken   ct
    ) {
        var page    = new List<T>(pageSize);
        var matched = 0;
        var scanned = 0;
        var hasMore = false;

        await foreach (var item in source.WithCancellation(ct)) {
            if (++scanned > cap) {
                throw new InvalidOperationException(
                    $"Residual filter scan exceeded the maximum of {cap} rows.");
            }

            if (!residual(item)) {
                continue;
            }

            if (matched >= skip) {
                if (matched - skip < pageSize) {
                    page.Add(item);
                } else {
                    hasMore = true;
                    if (!countExact) {
                        return (page, true, null);
                    }
                }
            }

            matched++;
        }

        return (page, hasMore, countExact ? matched : null);
    }
}
