namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     The kinds of BPMN catch event that need a party outside the engine to deliver them.
/// </summary>
public enum FlowCatchKind
{
    /// <summary>A message catch, correlated to one token.</summary>
    Message,

    /// <summary>A signal catch, broadcast across the process.</summary>
    Signal,

    /// <summary>A timer catch, fired by the scheduler.</summary>
    Timer,
}