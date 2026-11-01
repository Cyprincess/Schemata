using Schemata.Abstractions.Entities;

namespace Schemata.Push.Http.Integration.Tests.Fixtures;

/// <summary>Stand-in owner resource so the polymorphic <c>Owner</c> reference resolves <c>users/...</c>.</summary>
[CanonicalName("users/{subject}")]
public sealed class TestSubject : ICanonicalName
{
    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }
}
