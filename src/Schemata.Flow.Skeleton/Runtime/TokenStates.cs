using System;
using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Shared token lifecycle state sets used by Flow runtimes and handlers.</summary>
public static class TokenStates
{
    /// <summary>Terminal token states; runtimes skip these tokens when advancing work.</summary>
    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Completed",
        "Cancelled",
        "Failed",
        "Compensated",
    };

    /// <summary>States that can still consume runtime work.</summary>
    public static readonly IReadOnlySet<string> Live = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Active",
        "Waiting",
    };

    /// <summary>States counted as present at a BPMN join.</summary>
    public static readonly IReadOnlySet<string> JoinCounted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Waiting",
        "Failed",
    };

    /// <summary>Returns whether <paramref name="state" /> is terminal.</summary>
    public static bool IsTerminal(string? state) { return state is { } s && Terminal.Contains(s); }
}
