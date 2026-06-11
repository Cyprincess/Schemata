using System;

namespace Schemata.Scheduling.Skeleton;

public sealed class SchemataOperationMetadata
{
    public string? Job { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }
}