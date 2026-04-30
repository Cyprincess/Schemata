using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Owner;

/// <summary>
///     Resolves the canonical name of the principal that should own a newly created
///     <typeparamref name="TEntity" />, or the principal whose entities should be visible to the current
///     query. The contract is entity-typed so applications can provide alternate strategies per entity
///     (e.g., tenant-wide ownership, delegated ownership).
/// </summary>
/// <typeparam name="TEntity">The entity type whose owner is being resolved.</typeparam>
public interface IOwnerResolver<TEntity>
{
    /// <summary>
    ///     Resolves the owner canonical name for the current request, or <see langword="null" /> when no
    ///     owner can be determined (e.g., unauthenticated request).
    /// </summary>
    ValueTask<string?> ResolveAsync(CancellationToken ct);
}
