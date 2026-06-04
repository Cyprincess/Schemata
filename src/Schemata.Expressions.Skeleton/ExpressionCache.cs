using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Schemata.Expressions.Skeleton;

public static class ExpressionCache
{
    private static readonly LruCache<ExpressionCacheKey, IExpressionTree>  Trees       = new(500);
    private static readonly LruCache<ExpressionCacheKey, LambdaExpression> Expressions = new(500);

    private static readonly LruCache<LambdaExpression, Delegate> Delegates = new(200, LambdaReferenceComparer.Instance);

    public static IExpressionTree GetOrAddTree(ExpressionCacheKey key, Func<IExpressionTree> factory) {
        return Trees.GetOrAdd(key, factory);
    }

    public static Expression<Func<TContext, TResult>> GetOrAddExpression<TContext, TResult>(
        ExpressionCacheKey                        key,
        Func<Expression<Func<TContext, TResult>>> factory
    ) {
        return (Expression<Func<TContext, TResult>>)Expressions.GetOrAdd(key, factory);
    }

    public static Func<TContext, TResult> GetOrAddDelegate<TContext, TResult>(
        Expression<Func<TContext, TResult>> expression
    ) {
        if (expression is null) {
            throw new ArgumentNullException(nameof(expression));
        }

        return (Func<TContext, TResult>)Delegates.GetOrAdd(expression, expression.Compile);
    }

    #region Nested type: LambdaReferenceComparer

    private sealed class LambdaReferenceComparer : IEqualityComparer<LambdaExpression>
    {
        public static readonly LambdaReferenceComparer Instance = new();

        #region IEqualityComparer<LambdaExpression> Members

        public bool Equals(LambdaExpression? x, LambdaExpression? y) { return ReferenceEquals(x, y); }

        public int GetHashCode(LambdaExpression obj) { return RuntimeHelpers.GetHashCode(obj); }

        #endregion
    }

    #endregion
}
