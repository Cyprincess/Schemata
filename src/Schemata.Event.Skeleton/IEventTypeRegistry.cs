using System;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Maps event/request CLR types to wire names used as transport routing keys.
/// </summary>
/// <remarks>
///     <para>
///         Event names form a distributed contract owned by the application configuration. The
///         registry forces an explicit name per type so the wire shape survives type renames,
///         refactors, and cross-service deployments.
///     </para>
///     <para>
///         Implementations must be thread-safe for concurrent reads and one-time writes during
///         startup; mutating registrations at runtime is not supported.
///     </para>
/// </remarks>
public interface IEventTypeRegistry
{
    /// <summary>Registers <paramref name="type" /> under <paramref name="name" />.</summary>
    void Register(Type type, string name);

    /// <summary>Returns the registered name for <paramref name="type" />, or <see langword="null" />.</summary>
    string? GetName(Type type);

    /// <summary>Returns the registered type for <paramref name="name" />, or <see langword="null" />.</summary>
    Type? Resolve(string name);

    /// <summary>
    ///     Returns the registered name for <paramref name="type" /> or throws
    ///     <see cref="InvalidOperationException" /> when unregistered. Use at publish boundaries
    ///     where a missing registration is a programmer error.
    /// </summary>
    string RequireName(Type type);
}
