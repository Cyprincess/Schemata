namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-136 custom-method verb constants for Scheduling operations, carried by the
///     <see cref="Schemata.Messaging.Skeleton.Commands.ResourceMethodRequest{TEntity,TRequest,TResponse}" />
///     envelopes.
/// </summary>
public static class SchedulingOperations
{
    /// <summary>Triggers a job, per AIP-152.</summary>
    public const string Trigger = "trigger";
}
