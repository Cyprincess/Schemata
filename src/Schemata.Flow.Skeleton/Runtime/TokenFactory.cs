using System;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral helpers that build and mutate <typeparamref name="TToken" /> and
///     <typeparamref name="TProcess" /> rows from a resolved <see cref="TargetState" />. The
///     state-machine engine and the BPMN engine share scope wiring and lifecycle state strings.
/// </summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
public static class TokenFactory<TProcess, TToken>
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken, new()
{
    /// <summary>
    ///     Creates a root token (no <see cref="SchemataProcessToken.Spawner" />) sitting in the process
    ///     root scope. Used at <c>StartAsync</c> when a process instance is first created.
    /// </summary>
    /// <param name="process">The owning process aggregate.</param>
    /// <param name="resolved">The target state the engine resolved for the first element.</param>
    /// <returns>An unnamed token in the root scope, ready for repository creation.</returns>
    public static TToken NewRootToken(TProcess process, TargetState resolved) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(resolved);

        return new() {
            Process       = process.Name!,
            Spawner       = null,
            ScopeName     = string.Empty,
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
    public static TToken NewChildToken(TProcess process, TargetState resolved, TToken spawner) {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(spawner);

        return new() {
            Process       = process.Name!,
            Spawner       = spawner.CanonicalName,
            ScopeName     = spawner.ScopeName,
            StateName     = resolved.StateName,
            WaitingAtName = resolved.WaitingAtName,
            State         = TokenAggregator.TokenStateFor(resolved),
        };
    }
}
