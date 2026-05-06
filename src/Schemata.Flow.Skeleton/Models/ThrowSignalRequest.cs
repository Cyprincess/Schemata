namespace Schemata.Flow.Skeleton.Models;

public sealed class ThrowSignalRequest
{
    public string SignalName { get; set; } = null!;
    public string? Payload { get; set; }
}
