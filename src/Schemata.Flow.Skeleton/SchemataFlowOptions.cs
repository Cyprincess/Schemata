using System.Collections.Generic;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton;

/// <summary>
///     Options for the Schemata flow module.
/// </summary>
public class SchemataFlowOptions
{
    /// <summary>
    ///     Process configurations registered with the flow system.
    /// </summary>
    public List<ProcessConfiguration> Configurations { get; set; } = [];
}
