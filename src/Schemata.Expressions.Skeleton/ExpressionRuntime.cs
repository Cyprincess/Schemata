using System;
using System.Linq.Expressions;

namespace Schemata.Expressions.Skeleton;

public static class ExpressionRuntime
{
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
