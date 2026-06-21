namespace Schemata.Expressions.Skeleton;

/// <summary>
///     The result of splitting a parsed expression into a part pushed to the backend query and a
///     remainder evaluated locally. The split preserves the original semantics: the pushed part is a
///     weakening of the original (every row the original keeps, the pushed part keeps), and the
///     pushed part combined with the residual is equivalent to the original.
/// </summary>
/// <param name="Pushed">The translatable part to apply on the backend query, or null when nothing pushes.</param>
/// <param name="Residual">The part to evaluate locally, or null when the whole expression pushes.</param>
public sealed record ExpressionPushdownPlan(IExpressionTree? Pushed, IExpressionTree? Residual)
{
    /// <summary>
    ///     Gets whether a local residual must be applied after the backend query.
    /// </summary>
    public bool HasResidual => Residual is not null;

    /// <summary>
    ///     Gets whether any part of the expression is pushed to the backend query.
    /// </summary>
    public bool HasPushed => Pushed is not null;
}
