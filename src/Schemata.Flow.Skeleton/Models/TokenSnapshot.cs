namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Immutable read-only view of a single process token, decoupled from the mutable
///     <c>SchemataProcessToken</c> entity. Carried by <c>FlowTransitionContext</c>,
///     observer hook signatures, and transport response payloads.
/// </summary>
public sealed class TokenSnapshot
{
    /// <summary>Full AIP canonical name, e.g. <c>processes/{p}/tokens/{t}</c>.</summary>
    public required string CanonicalName { get; init; }

    /// <summary>Owning scope key: the process instance name at root, or the SubProcess / Transaction / EventSubProcess element name for BPMN sub-scopes.</summary>
    public required string ScopeName { get; init; }

    /// <summary>Name of the element the token sits on.</summary>
    public required string StateName { get; init; }

    /// <summary>Name of the waiting element; non-null when the token is suspended at a catch event or boundary.</summary>
    public string? WaitingAtName { get; init; }

    /// <summary>Source token canonical name that spawned this token. Always <see langword="null" /> on the state-machine engine.</summary>
    public string? Spawner { get; init; }

    /// <summary>Lifecycle state: <c>Active</c> / <c>Waiting</c> / <c>Completed</c> / <c>Failed</c> / <c>Cancelled</c> / <c>Compensating</c> / <c>Compensated</c>.</summary>
    public required string Status { get; init; }

}
