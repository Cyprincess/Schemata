using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceUpdateTimestamp{TEntity}" />.</summary>
public static class AdviceUpdateTimestamp
{
    /// <summary>Default execution order: 100,000,000.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets <see cref="ITimestamp.UpdateTime" /> to <see cref="DateTime.UtcNow" /> when an
///     entity is updated, per
///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     Runs first in the update pipeline. Only activates for entities implementing
///     <see cref="ITimestamp" />. Suppressed by <see cref="TimestampSuppressed" />.
/// </remarks>
public sealed class AdviceUpdateTimestamp<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateTimestamp.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<TimestampSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not ITimestamp time) {
            return Task.FromResult(AdviseResult.Continue);
        }

        time.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
