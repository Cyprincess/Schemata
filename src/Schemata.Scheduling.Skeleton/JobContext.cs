using System.Collections.Generic;

namespace Schemata.Scheduling.Skeleton;

public class JobContext
{
    public string JobName { get; set; } = null!;

    public IReadOnlyDictionary<string, object?> Variables { get; set; } = new Dictionary<string, object?>();
}
