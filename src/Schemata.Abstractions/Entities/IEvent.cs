namespace Schemata.Abstractions.Entities;

public interface IEvent
{
    string Event { get; set; }

    string? Note { get; set; }

    long? UpdatedById { get; set; }

    string? UpdatedBy { get; set; }
}
