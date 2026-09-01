using System;
using Microsoft.AspNetCore.WebUtilities;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Computes a weak ETag (<c>W/"..."</c>) from the detail's mapped
///     <see cref="IConcurrency.Timestamp" /> per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
///     Returns <see langword="null" /> unless the detail opts into freshness and carries a non-empty
///     timestamp.
/// </summary>
public sealed class DefaultEntityTagProvider : IEntityTagProvider
{
    #region IEntityTagProvider Members

    public string? GetEntityTag<TEntity, TDetail>(TDetail? detail, AdviceContext ctx)
        where TEntity : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        if (detail is not (IFreshness and IConcurrency concurrency)) {
            return null;
        }

        if (concurrency.Timestamp == Guid.Empty) {
            return null;
        }

        return $"W/\"{WebEncoders.Base64UrlEncode(concurrency.Timestamp.ToByteArray())}\"";
    }

    #endregion
}
