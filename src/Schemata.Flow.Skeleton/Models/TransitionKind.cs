namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Classifies a <see cref="Entities.SchemataProcessTransition" /> row by the kind of token
///     movement it records. The state-machine engine only ever writes the three engine-neutral
///     kinds (<see cref="Move" />, <see cref="Cancel" />, <see cref="Fail" />); the BPMN engine
///     additionally writes the four BPMN-only kinds when forks, joins, sub-process spawns, or
///     compensation handlers run.
/// </summary>
public enum TransitionKind
{
    /// <summary>Single-step token advance. The state-machine engine default; the BPMN engine common case.</summary>
    Move = 0,

    /// <summary>Token or process explicitly cancelled (boundary interrupt, process termination, scope cancel).</summary>
    Cancel = 1,

    /// <summary>Advisor or activity threw; token transitions to <c>Failed</c>.</summary>
    Fail = 2,

    /// <summary>BPMN-only: parallel / inclusive gateway split produced new child tokens.</summary>
    Fork = 3,

    /// <summary>BPMN-only: parallel join / inclusive merge collapsed sibling tokens into a single output token.</summary>
    Join = 4,

    /// <summary>BPMN-only: a <c>CallActivity</c> invoked a child process.</summary>
    Spawn = 5,

    /// <summary>BPMN-only: a transaction sub-process triggered a compensation handler.</summary>
    Compensate = 6,
}
