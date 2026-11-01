namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Lifecycle state of a <see cref="FlowEffectIntent" /> record.</summary>
public enum FlowEffectState
{
    /// <summary>The intent is recorded; the external effect has not been confirmed.</summary>
    Recorded,

    /// <summary>The task body completed and the completion committed with the transition.</summary>
    Completed,

    /// <summary>The task handler reported the external outcome as unknown.</summary>
    OutcomeUnknown,
}
