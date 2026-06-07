using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Multiple event definition — for catch events: fires when <em>any</em>
///     of the contained definitions triggers (XOR semantics).
///     For throw events: all definitions fire (AND semantics).
/// </summary>
public sealed class MultipleDefinition : IEventDefinition
{
    /// <summary>
    ///     The contained event definitions. At catch: first match wins.
    ///     At throw: all are fired.
    /// </summary>
    public List<IEventDefinition> Definitions { get; } = [];

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
