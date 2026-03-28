using System;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

internal static class FreshnessHelper
{
    public static bool TryGetEntityTag<TEntity>(AdviceContext ctx, TEntity? entity, out string? tag)
        where TEntity : class, ICanonicalName {
        tag = null;

        if (ctx.Has<FreshnessSuppressed>()) {
            return false;
        }

        if (entity is not IConcurrency concurrency) {
            return false;
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return false;
        }

        tag = $"W/\"{concurrency.Timestamp.Value.ToByteArray().ToBase64UrlString()}\"";

        return true;
    }
}
