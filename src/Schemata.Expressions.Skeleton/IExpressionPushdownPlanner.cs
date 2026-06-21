namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Splits a parsed expression into a backend-pushable part and a local residual, given the
///     backend's translation capabilities.
/// </summary>
/// <remarks>
///     A construct is pushable only when the backend both supports it (per
///     <see cref="ExpressionCapabilities" />) and its translation preserves the language's evaluation
///     semantics — null-chain handling, error behaviour, and function semantics. Constructs that are
///     ambiguous under the backend's three-valued logic, can raise language-defined errors, or have
///     no local evaluation are placed in the residual. Implementations stay conservative: when
///     pushability cannot be proven, the construct moves to the residual, which is always correct and
///     only costs local evaluation.
/// </remarks>
public interface IExpressionPushdownPlanner
{
    /// <summary>
    ///     Gets the language identifier handled by this planner.
    /// </summary>
    string Language { get; }

    /// <summary>
    ///     Splits a parsed tree into a pushable part and a local residual.
    /// </summary>
    ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities);
}
