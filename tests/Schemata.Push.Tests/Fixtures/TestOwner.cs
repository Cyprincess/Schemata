using Schemata.Abstractions.Entities;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>Stand-in owner resource for polymorphic subscription-owner references.</summary>
[CanonicalName("owners/{owner}")]
public sealed class TestOwner : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
