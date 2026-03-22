using System.ComponentModel.DataAnnotations;
using Schemata.Abstractions.Entities;

namespace Schemata.Core.Tests.Fixtures;

[CanonicalName("publishers/{publisher}/books/{book}")]
public class Book
{
    [Key]
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? PublisherName { get; set; }
}
