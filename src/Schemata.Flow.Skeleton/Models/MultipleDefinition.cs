using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Multiple event definition - for catch events: fires when <em>any</em>
///     of the contained definitions triggers (XOR semantics).
///     For throw events: all definitions fire (AND semantics).
/// </summary>
public sealed class MultipleDefinition : FlowGraphNode, IEventDefinition
{
    private string _name = null!;

    /// <summary>
    ///     The contained event definitions. At catch: first match wins.
    ///     At throw: all are fired.
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
