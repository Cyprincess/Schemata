using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>High-level facade for starting and interacting with process instances.</summary>
public interface IProcessRuntime
{
    /// <summary>Starts a new process instance from the registered definition.</summary>
    /// <param name="processName">The name of the registered process definition.</param>
    /// <param name="variables">Optional initial variables.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<SchemataProcess> StartProcessInstanceAsync(
        string                                processName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    );

    /// <summary>Completes the current activity and auto-advances the process instance.</summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="variables">Optional variables merged into the process instance.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<ProcessInstance> CompleteActivityAsync(
        string                                instanceName,
        IReadOnlyDictionary<string, object?>? variables = null,
        ClaimsPrincipal?                      principal = null,
        CancellationToken                     ct        = default
    );

    /// <summary>Correlates a named message to a specific process instance.</summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="messageName">The name of the <see cref="Message" /> definition.</param>
    /// <param name="payload">Optional payload merged into process variables.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<ProcessInstance> CorrelateMessageAsync(
        string            instanceName,
        string            messageName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>Broadcasts a signal to all waiting process instances.</summary>
    /// <param name="signalName">The name of the <see cref="Signal" /> definition.</param>
    /// <param name="payload">Optional payload merged into matched process variables.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ThrowSignalAsync(
        string            signalName,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>Triggers a generic event definition against a specific process instance.</summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="trigger">The event definition being triggered.</param>
    /// <param name="payload">Optional payload merged into process variables.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<ProcessInstance> TriggerEventAsync(
        string            instanceName,
        IEventDefinition  trigger,
        object?           payload   = null,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );

    /// <summary>Terminates a process instance immediately.</summary>
    /// <param name="instanceName">The canonical name of the process instance.</param>
    /// <param name="principal">Optional authenticated principal recorded in the audit log.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<ProcessInstance> TerminateProcessInstanceAsync(
        string            instanceName,
        ClaimsPrincipal?  principal = null,
        CancellationToken ct        = default
    );
}
