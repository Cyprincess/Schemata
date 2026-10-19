using System.Collections.Generic;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Conditional event definition - triggers when <see cref="Condition" />
///     evaluates to <c>true</c>.
/// </summary>
public sealed class ConditionalDefinition : FlowGraphNode, IEventDefinition
{
    private IConditionExpression _condition = null!;
    private string _name = null!;

    /// <summary>
    ///     The condition expression that must become <c>true</c> for this event to trigger.
    /// </summary>
    public IConditionExpression Condition {
        get => _condition;
        set {
            EnsureMutable();
            _condition = value;
        }
    }

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
        if (Condition is FlowGraphNode node) {
            node.Freeze();
        }
    }

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}
