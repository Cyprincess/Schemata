using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book : ICanonicalName
{
    public string? Publisher { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
