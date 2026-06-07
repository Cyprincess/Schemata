using System;
using System.Collections.Generic;
using Schemata.Event.Skeleton;

namespace Schemata.Event.Foundation;

/// <summary>Options for the event subsystem, including per-event routing configuration.</summary>
public class SchemataEventOptions
{
    /// <summary>Maps an event CLR type to its <see cref="EventRouting"/> mode; default is <see cref="EventRouting.Broadcast"/>.</summary>
    public Dictionary<Type, EventRouting> RoutingTable { get; } = new();
}
