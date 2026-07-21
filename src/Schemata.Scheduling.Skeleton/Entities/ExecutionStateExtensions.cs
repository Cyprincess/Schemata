namespace Schemata.Scheduling.Skeleton.Entities;

/// <summary>Extension helpers for <see cref="ExecutionState" />.</summary>
public static class ExecutionStateExtensions
{
    /// <summary>
    ///     Determines whether the execution has reached a terminal state:
    ///     <see cref="ExecutionState.Succeeded" />, <see cref="ExecutionState.Failed" />,
    ///     or <see cref="ExecutionState.Cancelled" />.
    /// </summary>
    /// <param name="state">The execution state to test.</param>
    /// <returns><see langword="true" /> if the state is terminal; otherwise <see langword="false" />.</returns>
    public static bool IsTerminal(this ExecutionState state) {
        return state is ExecutionState.Succeeded or ExecutionState.Failed or ExecutionState.Cancelled;
    }
}
