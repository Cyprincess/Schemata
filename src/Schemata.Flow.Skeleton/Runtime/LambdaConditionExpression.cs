using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression whose evaluation is delegated to a compiled lambda.
/// </summary>
public sealed class LambdaConditionExpression : FlowGraphNode, IConditionExpression
{
    private Func<FlowConditionContext, ValueTask<bool>>? _lambda;

    /// <summary>
    ///     Delegate that evaluates the condition against a <see cref="FlowConditionContext" />.
    /// </summary>
    public required Func<FlowConditionContext, ValueTask<bool>> Lambda {
        get => _lambda ?? throw new InvalidOperationException("The condition lambda has not been assigned.");
        set {
            EnsureMutable();
            _lambda = value;
        }
    }

    #region IConditionExpression Members

    public ValueTask<bool> Evaluate(FlowConditionContext context) { return Lambda(context); }

    #endregion
}
