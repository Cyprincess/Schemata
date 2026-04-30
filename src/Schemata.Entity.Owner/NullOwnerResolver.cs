using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Owner;

/// <summary>
///     Fallback <see cref="IOwnerResolver{TEntity}" /> that always returns <see langword="null" />.
///     Registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" /> so the
///     owner advisors can be resolved in setups that do not plug in the resource-layer resolver (e.g., background
///     jobs, tests). When used, the owner advisors leave the entity and the query untouched.
/// </summary>
/// <typeparam name="TEntity">The entity type whose owner is being resolved.</typeparam>
public sealed class NullOwnerResolver<TEntity> : IOwnerResolver<TEntity>
{
    #region IOwnerResolver<TEntity> Members

    /// <inheritdoc />
    public ValueTask<string?> ResolveAsync(CancellationToken ct) {
        return new((string?)null);
    }

    #endregion
}
