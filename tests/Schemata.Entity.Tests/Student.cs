using System.ComponentModel.DataAnnotations;

namespace Schemata.Entity.Tests;

public class Student
{
    [Key]
    public long Id { get; set; }

    public string? Name { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }
}
