using System;
using System.Threading.Tasks;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression whose evaluation is delegated to a compiled lambda.
/// </summary>
public sealed class LambdaConditionExpression : IConditionExpression
{
    /// <summary>
    ///     Delegate that evaluates the condition against a <see cref="FlowConditionContext" />.
    /// </summary>
    public Func<FlowConditionContext, ValueTask<bool>> Lambda { get; set; } = null!;

    #region IConditionExpression Members

    public ValueTask<bool> Evaluate(FlowConditionContext context) { return Lambda(context); }

    #endregion
}
