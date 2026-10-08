using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Sequence Flow connecting two <see cref="FlowElement" />s.
///     <see cref="Source" /> and <see cref="Target" /> hold direct object
///     references so the engine matches by identity during graph traversal.
/// </summary>
public sealed class SequenceFlow : FlowGraphNode, IDescriptive
{
    private FlowElement _source = null!;
    private FlowElement _target = null!;
    private IConditionExpression? _condition;
    private bool _isDefault;

    public FlowElement Source {
        get => _source;
        set {
            EnsureMutable();
            _source = value;
        }
    }

    public FlowElement Target {
        get => _target;
        set {
            EnsureMutable();
            _target = value;
        }
    }

    /// <summary>Optional guard expression; when present, the flow is only taken if the condition evaluates to true.</summary>
    public IConditionExpression? Condition {
        get => _condition;
        set {
            EnsureMutable();
            _condition = value;
        }
    }

    /// <summary>Indicates that this flow is the gateway fallback after sibling conditions fail.</summary>
    public bool IsDefault {
        get => _isDefault;
        set {
            EnsureMutable();
            _isDefault = value;
        }
    }

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        Source?.Freeze();
        Target?.Freeze();
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
