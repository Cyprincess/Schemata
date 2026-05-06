using System.Threading.Tasks;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Defines a condition expression evaluated during flow traversal
///     by exclusive and inclusive gateways.
/// </summary>
public interface IConditionExpression
{
    /// <summary>
    ///     Evaluates the condition against the provided <paramref name="context" />.
    /// </summary>
    /// <param name="context">
    ///     The <see cref="FlowConditionContext" /> containing the definition,
    ///     instance, and variables relevant to this evaluation.
    /// </param>
    /// <returns>
    ///     A <c>ValueTask&lt;bool&gt;</c> that completes with <c>true</c>
    ///     when the condition is satisfied.
    /// </returns>
    ValueTask<bool> Evaluate(FlowConditionContext context);
}
