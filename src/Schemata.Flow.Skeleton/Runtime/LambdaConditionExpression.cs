using System;
using System.Threading.Tasks;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     An <see cref="IConditionExpression" /> backed by a compiled delegate.
///     For typed condition expressions <c>When&lt;T&gt;(...)</c>, the lambda
///     receives a <see cref="FlowConditionContext" /> and deserializes the
///     typed variable value from the
///     <see cref="FlowConditionContext.Variables" /> dictionary before
///     invoking the compiled <see cref="Func{T,TResult}" />.
/// </summary>
public sealed class LambdaConditionExpression : IConditionExpression
{
    /// <summary>
    ///     The asynchronous delegate that performs the actual evaluation.
    ///     It receives the <see cref="FlowConditionContext" /> and returns
    ///     <c>true</c> when the condition is satisfied.
    /// </summary>
    public Func<FlowConditionContext, ValueTask<bool>> Lambda { get; set; } = null!;

    #region IConditionExpression Members

    public ValueTask<bool> Evaluate(FlowConditionContext context) { return Lambda(context); }

    #endregion
}
