using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Compensation event definition - triggers or throws compensation
///     for the activity referenced by <see cref="Activity" />.
/// </summary>
public sealed class CompensationDefinition : FlowGraphNode, IEventDefinition
{
    private Activity? _activity;
    private string _name = null!;

    /// <summary>
    ///     The activity whose compensation handler should be invoked.
    /// </summary>
    public Activity? Activity {
        get => _activity;
        set {
            EnsureMutable();
            _activity = value;
        }
    }

    public string Name {
        get => _name;
        set {
            EnsureMutable();
            _name = value;
        }
    }

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        Activity?.Freeze();
    }

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
