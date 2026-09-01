using Schemata.Domain.Skeleton;
using Schemata.Event.Skeleton;

namespace Schemata.Domain.Skeleton.Tests.Fixtures;

/// <summary>A domain event carrying enough state to tell two raises apart.</summary>
public sealed record WidgetRenamed(string Name) : IDomainEvent;

/// <summary>
///     Minimal aggregate exposing <see cref="AggregateBase.Raise" /> through a domain operation, so
///     the tests drive the buffer the way production code does rather than calling the protected
///     member directly.
/// </summary>
public sealed class SampleAggregate : AggregateBase
{
    public string Name { get; private set; } = string.Empty;

    public void Rename(string name) {
        Name = name;
        Raise(new WidgetRenamed(name));
    }
}
