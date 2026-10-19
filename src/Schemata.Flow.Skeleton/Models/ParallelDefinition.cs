using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Parallel Multiple event definition - all contained definitions
///     must trigger before the event fires (AND semantics, catch only).
/// </summary>
public sealed class ParallelDefinition : FlowGraphNode, IEventDefinition
{
    private string _name = null!;

    /// <summary>
    ///     The contained event definitions - all must match before the event fires.
    /// </summary>
    public FlowGraphCollection<IEventDefinition> Definitions { get; } = new();

    #region IEventDefinition Members

    public string Name {
        get => _name;
        set {
            EnsureMutable();
            _name = value;
        }
    }

    #endregion

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        Definitions.Freeze();
    }

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
