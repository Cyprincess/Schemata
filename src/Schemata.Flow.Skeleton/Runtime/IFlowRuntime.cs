using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Defines the contract for a flow runtime engine that can instantiate
///     processes, react to event triggers, and auto-advance through the
///     process graph.
/// </summary>
public interface IFlowRuntime
{
    /// <summary>
    ///     The unique name of this engine, used for registration and lookup.
    /// </summary>
    string EngineName { get; }

    /// <summary>
    ///     Creates a new process instance by locating the
    ///     <see cref="EventPosition.Start" /> event, following its outgoing
    ///     <see cref="SequenceFlow" />, and resolving the initial
    ///     <see cref="ProcessInstance.State" />.
    /// </summary>
    /// <param name="definition">The process definition to instantiate.</param>
    /// <param name="process">The entity representing the new instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     A <see cref="ProcessInstance" /> set to the initial activity state.
    /// </returns>
    ValueTask<ProcessInstance> StartAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Applies an event trigger to a running process instance. Matches
    ///     against event-based gateway branches and boundary events on the
    ///     current <see cref="ProcessInstance.State" />.
    /// </summary>
    /// <param name="definition">The process definition.</param>
    /// <param name="process">The entity representing the running instance.</param>
    /// <param name="trigger">
    ///     The event definition being triggered (e.g. a <see cref="Message" /> or
    ///     <see cref="ErrorDefinition" />).
    /// </param>
    /// <param name="payload">Optional payload data merged into process variables.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated process instance with the new state.</returns>
    /// <exception cref="Abstractions.Exceptions.InvalidArgumentException">
    ///     Thrown when the trigger does not match any waiting event or boundary event.
    /// </exception>
    ValueTask<ProcessInstance> TriggerAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        IEventDefinition  trigger,
        object?           payload,
        CancellationToken ct = default
    );

    /// <summary>
    ///     Auto-advances the process instance by following unconditional or
    ///     conditional outgoing <see cref="SequenceFlow" />s from the current state.
    ///     Returns the instance unchanged when <see cref="ProcessInstance.WaitingAt" />
    ///     is set (the instance is awaiting an external trigger).
    /// </summary>
    /// <param name="definition">The process definition.</param>
    /// <param name="process">The entity representing the running instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The process instance after zero or more automatic transitions.</returns>
    ValueTask<ProcessInstance> AdvanceAsync(
        ProcessDefinition definition,
        SchemataProcess   process,
        CancellationToken ct = default
    );
}
