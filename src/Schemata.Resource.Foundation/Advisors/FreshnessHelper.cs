using System;
using Microsoft.AspNetCore.WebUtilities;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared ETag construction for freshness advisors.
/// </summary>
internal static class FreshnessHelper
{
    /// <summary>
    ///     Builds a weak ETag (<c>W/"..."</c>) from the entity's <see cref="IConcurrency.Timestamp" />.
    ///     Returns <see langword="false" /> when freshness is suppressed, the entity lacks
    ///     <see cref="IConcurrency" />, or the timestamp is null/empty.
    /// </summary>
    public static bool TryGetEntityTag<TEntity>(AdviceContext ctx, TEntity? entity, out string? tag)
        where TEntity : class, ICanonicalName {
        tag = null;

        if (ctx.Has<FreshnessSuppressed>()) {
            return false;
        }

        if (entity is not IConcurrency concurrency) {
            return false;
        }

        if (concurrency.Timestamp == Guid.Empty) {
            return false;
        }

        tag = $"W/\"{WebEncoders.Base64UrlEncode(concurrency.Timestamp.ToByteArray())}\"";

        return true;
    }
}
