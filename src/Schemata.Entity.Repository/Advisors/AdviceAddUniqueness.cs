using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceAddUniqueness{TEntity}" />.
/// </summary>
public static class AdviceAddUniqueness
{
    /// <summary>
    ///     Default execution order: 170,000,000.
    /// </summary>
    public const int DefaultOrder = AdviceAddValidation.DefaultOrder + 30_000_000;
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
///         Skipped when the call passes <see cref="MutationOptions.IgnoreUniqueness" />.
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
        MutationOptions      options,
        CancellationToken    ct = default
    ) {
        if (options.IgnoreUniqueness) {
            return AdviseResult.Continue;
        }

        var existing = await repository.GetAsync(entity, ct, new QueryOptions(IgnoreSoftDelete: true));

        if (existing is not null) {
            var name = (entity as ICanonicalName)?.CanonicalName;
            throw SchemataResourceErrors.AlreadyExists<TEntity>(name);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
