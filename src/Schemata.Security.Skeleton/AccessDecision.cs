namespace Schemata.Security.Skeleton;

/// <summary>The outcome of an access evaluation, per AIP-211 denial semantics.</summary>
public enum AccessDecision
{
    /// <summary>The provider cannot decide with the facts it has; callers fail closed.</summary>
    Indeterminate = 0,

    /// <summary>The principal may perform the operation on the evaluated target.</summary>
    Allowed = 1,

    /// <summary>The principal is definitely denied; the denial is a real PERMISSION_DENIED.</summary>
    Denied = 2,
}