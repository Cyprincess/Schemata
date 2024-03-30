namespace Schemata.Abstractions.Entities;

public interface ICanonicalName
{
    string? Name { get; set; }

    string? CanonicalName { get; set; }
}
