using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression whose evaluation is delegated to a compiled lambda.
/// </summary>
public sealed class LambdaConditionExpression : FlowGraphNode, IConditionExpression
{
    private Func<FlowConditionContext, ValueTask<bool>> _lambda = null!;

    /// <summary>
    ///     Delegate that evaluates the condition against a <see cref="FlowConditionContext" />.
    /// </summary>
    public Func<FlowConditionContext, ValueTask<bool>> Lambda {
        get => _lambda;
        set {
            EnsureMutable();
            _lambda = value;
        }
    }

    #region IConditionExpression Members

    public ValueTask<bool> Evaluate(FlowConditionContext context) { return Lambda(context); }

    #endregion
}
