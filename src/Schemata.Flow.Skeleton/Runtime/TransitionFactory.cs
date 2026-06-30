using Schemata.Common;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Creates persisted transition rows emitted by Flow runtimes.</summary>
public static class TransitionFactory
{
    /// <summary>Creates a transition row with a fresh transition name.</summary>
    public static SchemataProcessTransition New(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) {
        return new() {
            Name      = Identifiers.NewUid().ToString("n"),
            Process   = processName,
            Token     = tokenCanonical,
            Kind      = kind,
            Previous  = previous,
            Posterior = posterior,
            Event     = eventName,
        };
    }
}
