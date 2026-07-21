namespace Schemata.Flow.Skeleton.Models;

/// <summary>Controls how a source binding receives process state projections.</summary>
public enum FlowSourceProjection
{
    /// <summary>Projects business state while active and process lifecycle after termination.</summary>
    Auto = 0,

    /// <summary>Projects business state and preserves it after process termination.</summary>
    BusinessState,

    /// <summary>Projects business state while active; after process termination projects the terminal process state.</summary>
    Terminal,

    /// <summary>Mirrors the binding scope lifecycle into the state member.</summary>
    Lifecycle,

    /// <summary>Disables state and lifecycle projection.</summary>
    None,
}
