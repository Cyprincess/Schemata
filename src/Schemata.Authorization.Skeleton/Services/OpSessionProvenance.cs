namespace Schemata.Authorization.Skeleton.Services;

/// <summary>How an OP-session evidence item was established. Higher values are more trusted.</summary>
public enum OpSessionProvenance
{
    /// <summary>Unverified browser state (cookie mirror). Reconciled to trusted identity, never authoritative over it.</summary>
    BrowserMirror = 0,

    /// <summary>Verified continuation of a previously issued session (persisted grant context).</summary>
    Continuation = 1,

    /// <summary>Verified host authentication ticket (the login the user actually performed).</summary>
    HostTicket = 2,
}