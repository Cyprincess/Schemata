using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Tests;

public class OrderEvent : IEvent
{
    public string Event { get; set; } = null!;

    public string? Note { get; set; }

    public long? UpdatedById { get; set; }

    public string? UpdatedBy { get; set; }
}
