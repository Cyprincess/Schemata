using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>Computes the response ETag for a resource; null means the resource does not opt into freshness.</summary>
public interface IEntityTagProvider
{
    /// <summary>
    ///     Computes the response ETag for <paramref name="detail" />
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type behind the response.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type carrying the response.</typeparam>
    /// <param name="detail">The mapped detail, or <see langword="null" /> when the response carries none.</param>
    /// <param name="ctx">The ambient advisor context for the dispatch.</param>
    /// <returns>The weak ETag to set on the detail, or <see langword="null" /> when the resource has no freshness tag.</returns>
    string? GetEntityTag<TEntity, TDetail>(TDetail? detail, AdviceContext ctx)
        where TEntity : class, ICanonicalName
        where TDetail : class, ICanonicalName;
}
