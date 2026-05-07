using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book
{
    public string? Name { get; set; }

    public string? PublisherName { get; set; }
}
