using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton;

/// <summary>
///     Represents the options used to configure the Schemata flow module, including the list of process configurations.
/// </summary>
public class SchemataFlowOptions
{
    /// <summary>
    ///     Gets or sets the list of process configurations to register with the flow system.
    /// </summary>
    public List<ProcessConfiguration> Configurations { get; set; } = [];
}
