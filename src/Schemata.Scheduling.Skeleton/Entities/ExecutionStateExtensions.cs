namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Extension helpers for <see cref="ExecutionState" />.</summary>
public static class ExecutionStateExtensions
{
    /// <summary>
    ///     Determines whether the execution has reached a terminal state:
    ///     <see cref="ExecutionState.Succeeded" />, <see cref="ExecutionState.Failed" />,
    ///     <see cref="ExecutionState.Cancelled" />, <see cref="ExecutionState.Blocked" />,
    ///     or <see cref="ExecutionState.Skipped" />. The settlement states
    ///     (<see cref="ExecutionState.Blocked" />, <see cref="ExecutionState.Skipped" />) close the
    ///     execution exactly like the outcome states, so operation reads, waits, cancellations, and
    ///     the operation wire envelope share one definition of finished.
    /// </summary>
    /// <param name="state">The execution state to test.</param>
    /// <returns><see langword="true" /> if the state is terminal; otherwise <see langword="false" />.</returns>
    public static bool IsTerminal(this ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled
            or ExecutionState.Blocked or ExecutionState.Skipped;
    }
}
