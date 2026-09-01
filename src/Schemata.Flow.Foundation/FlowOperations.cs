namespace Schemata.Flow.Foundation;

/// <summary>
///     AIP-136 custom-method verb constants for Flow process operations, matching the verbs declared
///     by the Flow resource registrations and carried by the
///     <see cref="Schemata.Messaging.Skeleton.Commands.ResourceMethodRequest{TEntity,TRequest,TResponse}" />
///     envelopes.
/// </summary>
public static class FlowOperations
{
    /// <summary>Starts a new process instance from a definition.</summary>
    public const string Start = "start";

    /// <summary>Completes the current activity of a process instance.</summary>
    public const string Complete = "complete";

    /// <summary>Correlates a named message to a process instance.</summary>
    public const string Correlate = "correlate";

    /// <summary>Broadcasts a signal to every waiting process instance.</summary>
    public const string Signal = "signal";

    /// <summary>Delivers a named signal to one candidate process.</summary>
    public const string Deliver = "deliver";

    /// <summary>Terminates a process instance.</summary>
    public const string Terminate = "terminate";

    /// <summary>Cancels one process token.</summary>
    public const string Cancel = "cancel";

    /// <summary>Runs an addressed internal Flow event.</summary>
    public const string RunEvent = "run-event";
}
