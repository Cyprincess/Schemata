using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddTimestamp{TEntity}" />.</summary>
public static class AdviceAddTimestamp
{
    /// <summary>Default execution order: 100,000,000.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets <see cref="ITimestamp.CreateTime" /> and <see cref="ITimestamp.UpdateTime" />
///     to <see cref="DateTime.UtcNow" /> when an entity is added, per
///     <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Runs first in the add pipeline. Only activates for entities implementing
///     <see cref="ITimestamp" />. Suppressed by <see cref="TimestampSuppressed" />.
/// </remarks>
public sealed class AdviceAddTimestamp<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddTimestamp.DefaultOrder;

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

        if (entity is not ITimestamp timestamp) {
            return Task.FromResult(AdviseResult.Continue);
        }

        timestamp.CreateTime = DateTime.UtcNow;
        timestamp.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
