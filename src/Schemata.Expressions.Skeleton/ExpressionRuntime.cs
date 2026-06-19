using System;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Evaluates compiled expression trees against runtime contexts.
/// </summary>
public static class ExpressionRuntime
{
    /// <summary>
    ///     Compiles or reuses a cached delegate and invokes it with the supplied context.
    /// </summary>
    public static TResult Evaluate<TContext, TResult>(
        Expression<Func<TContext, TResult>> expression,
        TContext                            context
    ) {
        if (expression is null) {
            throw new ArgumentNullException(nameof(expression));
        }

        var func = ExpressionCache.GetOrAddDelegate(expression);
        return func(context);
    }
}
