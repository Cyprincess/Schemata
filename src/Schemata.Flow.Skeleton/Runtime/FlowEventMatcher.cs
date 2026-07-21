using System;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Engine-neutral matching of a candidate <see cref="IEventDefinition" /> against an incoming
///     trigger, shared by every flow engine so boundary events, event-based gateway branches, and
///     intermediate catches resolve triggers identically. Implements the full BPMN 2.0.2 rule:
///     reference identity; <see cref="ErrorDefinition.ExceptionType" /> or name for errors;
///     escalation code, with a code-less catch matching any escalation; and name plus CLR type for
///     every other definition.
/// </summary>
public static class FlowEventMatcher
{
    /// <summary>
    ///     Determines whether <paramref name="candidate" /> is fired by <paramref name="trigger" />.
    /// </summary>
    /// <param name="candidate">
    ///     The event definition declared on a boundary event, event-based gateway branch, or
    ///     intermediate catch. A <see langword="null" /> candidate never matches.
    /// </param>
    /// <param name="trigger">The incoming trigger definition to match against.</param>
    public static bool Matches(IEventDefinition? candidate, IEventDefinition trigger) {
        if (candidate is null) {
            return false;
        }

        if (ReferenceEquals(candidate, trigger)) {
            return true;
        }

        if (candidate is ErrorDefinition expectedError && trigger is ErrorDefinition actualError) {
            return expectedError.ExceptionType == actualError.ExceptionType
                || string.Equals(expectedError.Name, actualError.Name, StringComparison.Ordinal);
        }

        if (candidate is EscalationDefinition expectedEscalation && trigger is EscalationDefinition actualEscalation) {
            if (expectedEscalation.EscalationCode is null) {
                return true;
            }

            return string.Equals(expectedEscalation.EscalationCode, actualEscalation.EscalationCode, StringComparison.Ordinal);
        }

        return string.Equals(candidate.Name, trigger.Name, StringComparison.Ordinal)
            && candidate.GetType() == trigger.GetType();
    }
}
