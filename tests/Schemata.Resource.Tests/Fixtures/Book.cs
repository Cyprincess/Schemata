using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Tests.Fixtures;

[CanonicalName("publishers/{publisher}/books/{book}")]
public sealed class Book
{
    public string? Publisher { get; set; }

    public string? Name { get; set; }
}
