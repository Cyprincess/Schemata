using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Skeleton.Models;

public sealed class ListProcessInstanceTransitionsResponse
{
    public List<SchemataProcessTransition> Transitions { get; set; } = new();
}
