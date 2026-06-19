using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceAddUniqueness{TEntity}" />.
/// </summary>
public static class AdviceAddUniqueness
{
    /// <summary>
    ///     Default order: runs last in the add chain, after the identity-assigning and
    ///     validation advisors, so the lookup sees the final key values.
    /// </summary>
    public const int DefaultOrder = AdviceAddValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Optimistic duplicate protection: before inserting, looks up the entity's key
///     values and throws <see cref="AlreadyExistsException" /> when a row already
///     exists, so duplicates surface as <c>ALREADY_EXISTS</c>
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>
///     and avoid provider-specific error-code translation.
/// </summary>
/// <remarks>
///     <para>
///         The protection is optimistic: a concurrent insert between the lookup and the
///         commit still surfaces as the provider's own constraint error.
///         Suppressed when <see cref="UniquenessSuppressed" /> is present.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
public sealed class AdviceAddUniqueness<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddUniqueness.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct = default
    ) {
        if (ctx.Has<UniquenessSuppressed>()) {
            return AdviseResult.Continue;
        }

        TEntity? existing;
        using (repository.SuppressQuerySoftDelete()) {
            existing = await repository.GetAsync(entity, ct);
        }

        if (existing is not null) {
            throw new AlreadyExistsException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
