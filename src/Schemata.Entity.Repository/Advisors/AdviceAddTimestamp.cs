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
///     to the current UTC time from the injected <see cref="TimeProvider" /> when an entity is
///     added, per <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Runs first in the add pipeline. Only activates for entities implementing
///     <see cref="ITimestamp" />. Suppressed by <see cref="TimestampSuppressed" />.
/// </remarks>
public sealed class AdviceAddTimestamp<TEntity>(TimeProvider? timeProvider = null) : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddTimestamp.DefaultOrder;

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

        // A single reading keeps CreateTime and UpdateTime equal on create per AIP-148.
        var now = _time.GetUtcNow().UtcDateTime;
        timestamp.CreateTime = now;
        timestamp.UpdateTime = now;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
