using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Parallel Multiple event definition — all contained definitions
///     must trigger before the event fires (AND semantics, catch only).
/// </summary>
public sealed class ParallelDefinition : IEventDefinition
{
    /// <summary>
    ///     The contained event definitions — all must match before the event fires.
    /// </summary>
    public List<IEventDefinition> Definitions { get; } = [];

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
