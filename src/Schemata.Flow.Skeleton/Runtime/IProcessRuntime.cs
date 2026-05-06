using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     High-level façade for starting and interacting with process instances
///     using BPMN 2.0 standard operations.
///     Coordinates <see cref="ProcessDefinition" /> lookup, engine selection,
///     variable serialization, and transition persistence through the unit-of-work.
/// </summary>
public interface IProcessRuntime
{
    /// <summary>
    ///     Starts a new process instance from the registered definition
    ///     identified by <paramref name="processName" />.
    /// </summary>
    /// <param name="processName">The name of the registered process definition.</param>
    /// <param name="variables">Optional initial variables to seed the process instance.</param>
    /// <param name="principal">Optional authenticated principal recorded in the transition audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="Entities.SchemataProcess" /> entity with the initial state.</returns>
    ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    );

    /// <summary>
    ///     Completes the current activity and auto-advances the process instance
    ///     through unconditional or conditional outgoing sequence flows.
    /// </summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="variables">Optional variables merged into the process instance.</param>
    /// <param name="principal">Optional authenticated principal recorded in the transition audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resulting <see cref="ProcessInstance" /> state after advancement.</returns>
    ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    );

    /// <summary>
    ///     Correlates a named message to a specific process instance.
    ///     Matches against intermediate catch events after event-based gateways
    ///     and message boundary events on the current activity.
    /// </summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="messageName">The name of the <see cref="Message" /> definition.</param>
    /// <param name="payload">Optional payload data merged into process variables.</param>
    /// <param name="principal">Optional authenticated principal for the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resulting <see cref="ProcessInstance" /> state after the correlation.</returns>
    ValueTask<ProcessInstance> CorrelateMessageAsync(
        string            instanceName,
        string            messageName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>
    ///     Throws (broadcasts) a signal to all process instances currently waiting at
    ///     an <see cref="EventPosition.IntermediateCatch" /> event whose definition is a matching
    ///     <see cref="Signal" />.
    /// </summary>
    /// <param name="signalName">The name of the <see cref="Signal" /> definition.</param>
    /// <param name="payload">Optional payload data merged into process variables.</param>
    /// <param name="principal">Optional authenticated principal for the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>
    ///     Terminates a process instance immediately, setting its state to terminated.
    /// </summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="principal">Optional authenticated principal for the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terminated <see cref="ProcessInstance" /> state.</returns>
    ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );
}
