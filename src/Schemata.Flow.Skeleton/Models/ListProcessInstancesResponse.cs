using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Models;

public sealed class ListProcessInstancesResponse
{
    public List<SchemataProcess> Processes { get; set; } = new();
}
