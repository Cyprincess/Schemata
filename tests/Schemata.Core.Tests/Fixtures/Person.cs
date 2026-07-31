using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("people/{person}")]
public class Person : ICanonicalName
{
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
