using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Validates the source entity contract used by event publish overloads that attach an
///     optimistic-snapshot source.
/// </summary>
public static class EventSourceContract
{
    /// <summary>
    ///     Validates that <paramref name="sourceEntity" /> implements both
    ///     <see cref="ICanonicalName" /> and <see cref="IConcurrency" />.
    /// </summary>
    public static void Ensure(object sourceEntity) {
        ArgumentNullException.ThrowIfNull(sourceEntity);

        if (sourceEntity is not ICanonicalName) {
            throw new InvalidOperationException(
                $"Event source '{sourceEntity.GetType().FullName}' must implement ICanonicalName "
              + "to be used as a publish source.");
        }

        if (sourceEntity is not IConcurrency) {
            throw new InvalidOperationException(
                $"Event source '{sourceEntity.GetType().FullName}' must implement IConcurrency "
              + "so the framework can capture an optimistic-snapshot timestamp for consumers.");
        }
    }
}
