using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Event.Advisors;

/// <summary>Order constants for <see cref="AdviceCommittedPendingEvents{TEntity}" />.</summary>
public static class AdviceCommittedPendingEvents
{
    /// <summary>
    ///     Default execution order within the resource segment: <see cref="Orders.Max" /> minus 1000.
    ///     Query-cache eviction is structural instead: it runs in the repository segment
    ///     (<see cref="Schemata.Entity.Repository.CommitOrders.Repository" />), which always precedes
    ///     the resource segment this advisor's callbacks are enlisted in.
    /// </summary>
    public const int DefaultOrder = Orders.Max - 1_000;
}

/// <summary>
///     Publishes the events buffered on <see cref="IHasPendingEvents" /> entities once their unit of
///     work has committed.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Prepare" /> retains the entity reference and returns the publishing callback;
///         the callback runs from the unit of work's commit sinks only, so a rolled-back transaction
///         never publishes. Buffering — rather than publishing at mutation time — is what keeps the
///         event stream consistent with the committed state.
///     </para>
///     <para>
///         When several mutations capture the same entity, only the first callback to run observes the
///         buffered events: draining dequeues them.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type whose committed mutations may carry pending events.</typeparam>
internal sealed class AdviceCommittedPendingEvents<TEntity> : IResourceMutationCommittedAdvisor<TEntity>
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

    #region IResourceMutationCommittedAdvisor<TEntity> Members

    public int Order => AdviceCommittedPendingEvents.DefaultOrder;

    public Func<CancellationToken, Task>? Prepare(TEntity entity, Operations operation) {
        if (entity is not IHasPendingEvents source) {
            return null;
        }

        return async ct => {
            foreach (var @event in source.DequeuePendingEvents()) {
                await _bus.PublishAsync(@event, ct);
            }
        };
    }

    #endregion
}
