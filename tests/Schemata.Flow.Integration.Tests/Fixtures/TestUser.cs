using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

/// <summary>Stand-in user resource for polymorphic flow references.</summary>
[CanonicalName("users/{user}")]
public sealed class TestUser : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
