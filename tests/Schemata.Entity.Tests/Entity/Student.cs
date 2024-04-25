using System.ComponentModel.DataAnnotations;

namespace Schemata.Entity.Tests.Entity;

public class Student
{
    [Key]
    public long Id { get; set; }

    public string? Name { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }
}
