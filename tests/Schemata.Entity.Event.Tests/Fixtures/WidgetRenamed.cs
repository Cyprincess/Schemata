using Schemata.Event.Skeleton;

namespace Schemata.Entity.Event.Tests.Fixtures;

/// <summary>A domain event carrying enough state to tell two raises apart.</summary>
public sealed record WidgetRenamed(string Name) : IEvent;