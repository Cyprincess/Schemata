using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

[Table("NullableKeyThings")]
[PrimaryKey(nameof(Id))]
public class NullableKeyThing
{
    public long?   Id   { get; set; }
    public string? Name { get; set; }
}
