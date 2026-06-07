using System;
using System.Collections.Generic;

namespace Schemata.Event.Foundation.Internal;

/// <summary>
///     Options-pattern shim carrying the registered (type, name) pairs from the
///     <see cref="Builders.EventBuilder" /> into <see cref="DefaultEventTypeRegistry" /> at
///     application start. Decouples the builder time (no DI scope yet) from the registry
///     resolution time (registry resolved from DI).
/// </summary>
public sealed class EventTypeRegistryConfiguration
{
    /// <summary>The accumulated (event type, wire name) pairs registered through <see cref="Builders.EventBuilder.RegisterEvent{TEvent}"/>.</summary>
    public List<(Type Type, string Name)> Registrations { get; } = new();
}