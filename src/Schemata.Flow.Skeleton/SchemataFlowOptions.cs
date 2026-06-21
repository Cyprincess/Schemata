using System.Collections.Generic;
using Schemata.Expressions.Skeleton;
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

    /// <summary>
    ///     When <c>true</c> (the default), each transition writes the process's runtime position back
    ///     to its source business entity within the transition's unit of work. Set to <c>false</c> to
    ///     persist process state and history only, leaving the source entity untouched.
    /// </summary>
    public bool SourceWriteback { get; set; } = true;

    /// <summary>
    ///     Gets or sets the expression languages this flow module enables, in priority order; the
    ///     first is the default when a condition omits an explicit language.
    /// </summary>
    public ExpressionLanguageProfile Expressions { get; set; } = new();
}
