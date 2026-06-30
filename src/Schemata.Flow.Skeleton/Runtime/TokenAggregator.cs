using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral helpers that derive the lifecycle state of a <see cref="SchemataProcessToken" />
///     or a <see cref="SchemataProcess" /> aggregate from a <see cref="TargetState" /> or from the current
///     set of live tokens. The state-machine and BPMN engines share these helpers so the
///     <see cref="SchemataProcess.State" /> aggregation rules (Completed / Running / Waiting / Failed /
///     Compensated) are not duplicated.
/// </summary>
public static class TokenAggregator
{
    /// <summary>
    ///     Mutates <paramref name="token" /> in place to reflect <paramref name="resolved" />: the new
    ///     <see cref="SchemataProcessToken.StateName" />, the optional catch element name, and the lifecycle
    ///     <see cref="SchemataProcessToken.State" /> string.
    /// </summary>
    public static void ApplyResolvedToToken(SchemataProcessToken token, TargetState resolved) {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(resolved);

        token.StateName     = resolved.StateName;
        token.WaitingAtName = resolved.WaitingAtName;
        token.State         = TokenStateFor(resolved);
    }

    /// <summary>
    ///     Mutates <paramref name="token" /> in place to reflect a one-element <paramref name="resolved" />,
    ///     then recomputes the aggregate state from the entire current token set. Encapsulates the
    ///     "apply + recompute" step so engine call sites stay short.
    /// </summary>
    public static void ApplyAndAggregate(
        SchemataProcess                     process,
        SchemataProcessToken                token,
        TargetState                         resolved,
        IReadOnlyList<SchemataProcessToken> allTokens
    ) {
        ArgumentNullException.ThrowIfNull(allTokens);
        ApplyResolvedToToken(token, resolved);
        ApplyAggregateState(process, allTokens);
    }

    /// <summary>
    ///     Recomputes <see cref="SchemataProcess.State" /> from the current token set:
    ///     <list type="bullet">
    ///         <item>no tokens → <c>Completed</c></item>
    ///         <item>all tokens terminal → <c>Failed</c> if any token failed, otherwise <c>Completed</c></item>
    ///         <item>any active → <c>Running</c></item>
    ///         <item>otherwise (only waiting tokens) → <c>Waiting</c></item>
    ///     </list>
    /// </summary>
    public static void ApplyAggregateState(SchemataProcess process, IReadOnlyList<SchemataProcessToken> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count == 0) {
            process.State = "Completed";
            return;
        }

        var allTerminal = tokens.All(t => TokenStates.IsTerminal(t.State));
        if (allTerminal) {
            process.State = tokens.Any(t => string.Equals(t.State, "Failed", StringComparison.OrdinalIgnoreCase))
                ? "Failed"
                : "Completed";
            return;
        }

        var anyActive  = tokens.Any(t => string.Equals(t.State, "Active", StringComparison.OrdinalIgnoreCase));
        var anyWaiting = tokens.Any(t => string.Equals(t.State, "Waiting", StringComparison.OrdinalIgnoreCase));
        process.State = (anyWaiting && !anyActive) ? "Waiting" : "Running";
    }

    /// <summary>
    ///     Maps a <see cref="TargetState" /> to the corresponding token lifecycle string:
    ///     <c>Completed</c> when the target consumes the token, <c>Waiting</c> when the target parks
    ///     the token at a catch event, otherwise <c>Active</c>.
    /// </summary>
    public static string TokenStateFor(TargetState resolved) {
        ArgumentNullException.ThrowIfNull(resolved);

        if (resolved.IsComplete) {
            return "Completed";
        }

        return resolved.WaitingAtName is not null ? "Waiting" : "Active";
    }
}
