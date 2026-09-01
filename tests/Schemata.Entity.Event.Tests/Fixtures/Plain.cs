namespace Schemata.Entity.Event.Tests.Fixtures;

/// <summary>An entity that buffers nothing, so the advisor must leave it alone.</summary>
public sealed class Plain
{
    public string Name { get; init; } = string.Empty;
}