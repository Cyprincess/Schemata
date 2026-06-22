using System.Collections.Generic;
using System.Linq;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Projects registered Flow process definitions into transport DTOs.</summary>
public sealed class ProcessDefinitionQueryService(IProcessRegistry registry)
{
    /// <summary>Lists registered Flow process definitions.</summary>
    public List<ProcessDefinitionInfo> ListProcessDefinitions() {
        return registry.GetRegisteredProcesses()
                       .Select(n => {
                           var definition = registry.GetRegistration(n)?.Definition;
                           return new ProcessDefinitionInfo {
                               CanonicalName = $"definitions/{n}",
                               DisplayName   = definition?.DisplayName,
                               Description   = definition?.Description,
                           };
                       })
                       .ToList();
    }
}
