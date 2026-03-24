using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Automatonymous;
using Automatonymous.Graphing;
using Schemata.Abstractions.Entities;
using Schemata.Workflow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Schemata.Workflow.Skeleton;

/// <summary>
/// Extension methods for raising events, querying next events, and generating graphs on <see cref="StateMachineBase{TI}"/>.
/// </summary>
public static class StateMachineBaseExtensions
{
    /// <summary>
    /// Raises a named event on the state machine for the given entity instance.
    /// </summary>
    /// <typeparam name="TI">The stateful entity type.</typeparam>
    /// <typeparam name="TEvent">The event data type, which must implement <see cref="IEvent"/>.</typeparam>
    /// <param name="machine">The state machine.</param>
    /// <param name="instance">The entity to transition.</param>
    /// <param name="event">The event data containing the event name and metadata.</param>
    /// <param name="ct">A cancellation token.</param>
    public static Task RaiseEventAsync<TI, TEvent>(
        this StateMachineBase<TI> machine,
        TI                        instance,
        TEvent                    @event,
        CancellationToken         ct
    )
        where TI : class, IStatefulEntity
        where TEvent : class, IEvent {
        ct.ThrowIfCancellationRequested();
        return machine.RaiseEvent(instance, _ => machine.GetEvent<TEvent>(@event.Event), @event, ct);
    }

    /// <summary>
    /// Gets the names of events that can be raised from the entity's current state.
    /// </summary>
    /// <typeparam name="TI">The stateful entity type.</typeparam>
    /// <param name="machine">The state machine.</param>
    /// <param name="instance">The entity to inspect.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A collection of available event names.</returns>
    public static async Task<IEnumerable<string>> GetNextEventsAsync<TI>(
        this StateMachineBase<TI> machine,
        TI                        instance,
        CancellationToken         ct
    )
        where TI : class, IStatefulEntity {
        ct.ThrowIfCancellationRequested();
        var events = await machine.NextEvents(instance);
        return events.Select(e => e.Name);
    }

    /// <summary>
    /// Gets the full state machine graph showing all states and transitions.
    /// </summary>
    /// <typeparam name="TI">The stateful entity type.</typeparam>
    /// <param name="machine">The state machine.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The <see cref="StateMachineGraph"/> representation.</returns>
    public static Task<StateMachineGraph> GetGraphAsync<TI>(this StateMachineBase<TI> machine, CancellationToken ct)
        where TI : class, IStatefulEntity {
        ct.ThrowIfCancellationRequested();
        var graph = machine.GetGraph();
        return Task.FromResult(graph);
    }
}
