using System;
using Schemata.Common;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral helpers that build and mutate <see cref="SchemataProcessToken" /> and
///     <see cref="SchemataProcess" /> rows from a resolved <see cref="TargetState" />. The state-machine
///     engine and the BPMN engine share these helpers so token canonical names, scope wiring, and
///     lifecycle state strings stay in one place.
/// </summary>
public static class TokenFactory
{
    /// <summary>
    ///     Creates a root token (no <see cref="SchemataProcessToken.Spawner" />) sitting in the process
    ///     root scope. Used at <c>StartAsync</c> when a process instance is first created.
    /// </summary>
    /// <param name="process">The owning process aggregate.</param>
    /// <param name="resolved">The target state the engine resolved for the first element.</param>
    /// <returns>A new token with a fresh leaf id and root scope.</returns>
    public static SchemataProcessToken NewRootToken(SchemataProcess process, TargetState resolved) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(resolved);

        var leaf      = Identifiers.NewUid().ToString("n");
        var canonical = $"{process.CanonicalName}/tokens/{leaf}";

        return new() {
            Name          = leaf,
            CanonicalName = canonical,
            Process       = process.Name!,
            Spawner       = null,
            ScopeName     = process.Name!,
            StateName     = resolved.StateName,
            WaitingAtName = resolved.WaitingAtName,
            State         = TokenAggregator.TokenStateFor(resolved),
        };
    }

    /// <summary>
    ///     Creates a child token that inherits its scope and spawner from <paramref name="spawner" />. Used
    ///     by the BPMN engine whenever a multi-token shape (fork, boundary, MI, sub-process) opens a new
    ///     execution path.
    /// </summary>
    public static SchemataProcessToken NewChildToken(
        SchemataProcess      process,
        TargetState          resolved,
        SchemataProcessToken spawner
    ) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(spawner);

        var leaf      = Identifiers.NewUid().ToString("n");
        var canonical = $"{process.CanonicalName}/tokens/{leaf}";

        return new() {
            Name          = leaf,
            CanonicalName = canonical,
            Process       = process.Name!,
            Spawner       = spawner.CanonicalName,
            ScopeName     = spawner.ScopeName,
            StateName     = resolved.StateName,
            WaitingAtName = resolved.WaitingAtName,
            State         = TokenAggregator.TokenStateFor(resolved),
        };
    }
}
