using System;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral helper that projects a <see cref="SchemataProcessToken" /> into the immutable
///     <see cref="TokenSnapshot" /> view consumed by advisor contexts, observer hooks, and
///     transport payloads. Shared by the state-machine and BPMN engines so the snapshot shape
///     stays in one place.
/// </summary>
public static class TokenSnapshotFactory
{
    /// <summary>Builds a <see cref="TokenSnapshot" /> from a token entity.</summary>
    public static TokenSnapshot From(SchemataProcessToken token) {
        ArgumentNullException.ThrowIfNull(token);

        return new() {
            CanonicalName = token.CanonicalName ?? string.Empty,
            ScopeName     = token.ScopeName,
            StateName     = token.StateName,
            WaitingAtName = token.WaitingAtName,
            Spawner       = token.Spawner,
            Status        = token.State ?? "Active",
        };
    }

    /// <summary>Builds a root-scope snapshot placeholder for process-level callbacks.</summary>
    public static TokenSnapshot From(SchemataProcess process) {
        ArgumentNullException.ThrowIfNull(process);

        return new() {
            CanonicalName = string.Empty,
            ScopeName     = process.Name ?? string.Empty,
            StateName     = string.Empty,
            Status        = "Active",
        };
    }
}
