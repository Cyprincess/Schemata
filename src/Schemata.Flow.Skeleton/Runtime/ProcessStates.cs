using System;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Shared process lifecycle state helpers.</summary>
public static class ProcessStates
{
    /// <summary>Returns whether <paramref name="state" /> represents a terminal process lifecycle state.</summary>
    public static bool IsTerminal(string? state) {
        return string.Equals(state, "Completed", StringComparison.Ordinal)
            || string.Equals(state, "Failed", StringComparison.Ordinal)
            || string.Equals(state, "Terminated", StringComparison.Ordinal)
            || string.Equals(state, "Cancelled", StringComparison.Ordinal);
    }
}
