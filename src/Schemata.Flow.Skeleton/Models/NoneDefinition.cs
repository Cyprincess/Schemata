using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A None event definition for plain Start and End events with an empty trigger payload.
/// </summary>
public sealed class NoneDefinition : FlowGraphNode, IEventDefinition
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
