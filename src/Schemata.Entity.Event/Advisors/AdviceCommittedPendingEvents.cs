using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Event.Advisors;

/// <summary>Order constants for <see cref="AdviceCommittedPendingEvents{TEntity}" />.</summary>
public static class AdviceCommittedPendingEvents
{
    /// <summary>
    ///     Default execution order: <see cref="Orders.Max" /> minus 1000, so pending events are
    ///     published before the query cache is evicted at <see cref="Orders.Max" />.
    /// </summary>
    public const int DefaultOrder = Orders.Max - 1_000;
}

/// <summary>
///     Publishes the events buffered on <see cref="IHasPendingEvents" /> entities once their unit of
///     work has committed.
/// </summary>
/// <remarks>
///     <para>
///         Committed advisors run from the unit of work's commit sink only; the rollback sink runs a
///         different path. A transaction that rolls back therefore never reaches this advisor, which
///         is what makes buffering — rather than publishing at mutation time — the correct shape.
///     </para>
///     <para>
///         All three change lists are walked. A removed aggregate can have raised events before it
///         was deleted, and dropping those would lose exactly the facts a consumer needs most.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type whose committed changes may carry pending events.</typeparam>
internal sealed class AdviceCommittedPendingEvents<TEntity> : IRepositoryCommittedAdvisor<TEntity>
    where TEntity : class
{
    private readonly IEventBus _bus;

    /// <summary>
    ///     Initializes the advisor with the bus the drained events are published to.
    /// </summary>
    /// <remarks>
    ///     <see cref="IEventBus" /> is a hard constructor dependency on purpose. This package is
    ///     usable without the Schemata feature pipeline, so it cannot rely on a startup-time
    ///     <c>DependsOn</c> check; requiring the bus here turns a missing registration into a clear
    ///     DI resolution failure on the first commit instead of silently dropping events.
    /// </remarks>
    /// <param name="bus">The event bus.</param>
    public AdviceCommittedPendingEvents(IEventBus bus) { _bus = bus; }

    #region IRepositoryCommittedAdvisor<TEntity> Members

    public int Order => AdviceCommittedPendingEvents.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        IRepository<TEntity>   repository,
        CommitChanges<TEntity> changes,
        CancellationToken      ct = default
    ) {
        foreach (var entity in changes.Added.Concat(changes.Updated).Concat(changes.Removed)) {
            if (entity is not IHasPendingEvents source) {
                continue;
            }

            foreach (var @event in source.DequeuePendingEvents()) {
                await _bus.PublishAsync(@event, ct);
            }
        }

        return AdviseResult.Continue;
    }

    #endregion
}
