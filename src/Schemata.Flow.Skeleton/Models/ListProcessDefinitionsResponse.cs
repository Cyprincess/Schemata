using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

public sealed class ListProcessDefinitionsResponse
{
    public List<string> Names { get; set; } = new();
}
