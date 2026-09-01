namespace Schemata.Domain.Skeleton.Tests.Fixtures;

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
