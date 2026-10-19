using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>Creates persisted transition rows emitted by Flow runtimes.</summary>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
public static class TransitionFactory<TTransition>
    where TTransition : SchemataProcessTransition, new()
{
    public static TTransition New(
        string         processName,
        string?        tokenCanonical,
        string?        previous,
        string?        posterior,
        TransitionKind kind,
        string         eventName
    ) {
        return new() {
            Process   = processName,
            Token     = tokenCanonical,
            Kind      = kind,
            Previous  = previous,
            Posterior = posterior,
            Event     = eventName,
        };
    }
}
