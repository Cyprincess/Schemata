using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Link event definition - a pair of Intermediate Catch and Throw events
///     connected by name. Acts as an off-page connector.
/// </summary>
public sealed class LinkDefinition : FlowGraphNode, IEventDefinition
{
    private string _name = null!;

    #region IEventDefinition Members

    public string Name {
        get => _name;
        set {
            EnsureMutable();
            _name = value;
        }
    }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
