using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceUpdateConcurrency{TEntity}" />.</summary>
public static class AdviceUpdateConcurrency
{
    /// <summary>Default execution order: 2,147,483,647.</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Verifies the concurrency stamp matches the stored value and generates a new stamp on
///     update, per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     Runs last in the update pipeline. Fetches the stored entity, compares
///     <see cref="IConcurrency.Timestamp" /> values, and throws
///     <see cref="ConcurrencyException" /> on mismatch. Suppressed by
///     <see cref="ConcurrencySuppressed" />.
/// </remarks>
public sealed class AdviceUpdateConcurrency<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    public int Order => AdviceUpdateConcurrency.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<ConcurrencySuppressed>()) {
            return AdviseResult.Continue;
        }

        if (entity is not IConcurrency concurrency) {
            return AdviseResult.Continue;
        }

        var stored = await repository.GetAsync<IConcurrency>(entity, ct);

        if (stored is null) {
            return AdviseResult.Continue;
        }

        if (stored.Timestamp != concurrency.Timestamp) {
            throw new ConcurrencyException();
        }

        concurrency.Timestamp = Guid.NewGuid();

        return AdviseResult.Continue;
    }

    #endregion
}
