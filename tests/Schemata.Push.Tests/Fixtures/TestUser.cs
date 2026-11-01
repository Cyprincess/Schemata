using Schemata.Abstractions.Entities;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>Stand-in user resource for polymorphic subscription-owner references.</summary>
[CanonicalName("users/{user}")]
public sealed class TestUser : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
