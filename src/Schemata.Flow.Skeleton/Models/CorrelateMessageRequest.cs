namespace Schemata.Flow.Skeleton.Models;

public sealed class CorrelateMessageRequest
{
    public string? InstanceName { get; set; }
    public string MessageName { get; set; } = null!;
    public string? Payload { get; set; }
}
