using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace System.Linq;

/// <summary>
///     Utility methods for building and combining predicate expressions.
/// </summary>
public static class Predicate
{
    /// <summary>
    ///     Creates a predicate that always evaluates to <see langword="true" />.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>An always-true predicate expression.</returns>
    public static Expression<Func<T, bool>> True<T>() { return q => true; }

    /// <summary>
    ///     Creates a predicate that always evaluates to <see langword="false" />.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <returns>An always-false predicate expression.</returns>
    public static Expression<Func<T, bool>> False<T>() { return q => false; }

    /// <summary>
    ///     Casts a predicate expression to a different result type by rebinding the parameter.
    /// </summary>
    /// <typeparam name="T">The source entity type.</typeparam>
    /// <typeparam name="TResult">The target entity type.</typeparam>
    /// <param name="predicate">The predicate to cast.</param>
    /// <returns>The rebound predicate, or <see langword="null" /> if the input is null.</returns>
    public static Expression<Func<TResult, bool>>? Cast<T, TResult>(Expression<Func<T, bool>>? predicate) {
        if (predicate is null) {
            return null;
        }

        var parameter = Expression.Parameter(typeof(T));

        var body = ExpressionReplacer.Replace(predicate, parameter);

        return Expression.Lambda<Func<TResult, bool>>(body!, parameter);
    }

    /// <summary>
    ///     Combines two predicate expressions with a logical AND.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="left">The left predicate.</param>
    /// <param name="right">The right predicate.</param>
    /// <returns>The combined predicate.</returns>
    public static Expression<Func<TEntity, bool>> And<TEntity>(
        this Expression<Func<TEntity, bool>>? left,
        Expression<Func<TEntity, bool>>?      right
    ) {
        return left.Combine(right, ExpressionType.AndAlso);
    }

    /// <summary>
    ///     Combines two predicate expressions with a logical OR.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="left">The left predicate.</param>
    /// <param name="right">The right predicate.</param>
    /// <returns>The combined predicate.</returns>
    public static Expression<Func<TEntity, bool>> Or<TEntity>(
        this Expression<Func<TEntity, bool>>? left,
        Expression<Func<TEntity, bool>>?      right
    ) {
        return left.Combine(right, ExpressionType.OrElse);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        this Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>>?      right,
        ExpressionType                  type
    ) {
        if (left is null && right is null) {
            return False<T>();
        }

        if (left is null) {
            return right!;
        }

        if (right is null) {
            return left;
        }

        var parameter = Expression.Parameter(typeof(T));

        var l = ExpressionReplacer.Replace(left, parameter);
        var r = ExpressionReplacer.Replace(right, parameter);

        var body = Expression.MakeBinary(type, l!, r!);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    #region Nested type: ExpressionReplacer

    private class ExpressionReplacer : ExpressionVisitor
    {
        private readonly Expression _newValue;
        private readonly Expression _oldValue;

        private ExpressionReplacer(Expression oldValue, Expression newValue) {
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public static Expression? Replace(LambdaExpression? expression, ParameterExpression parameter) {
            if (expression is null) {
                return null;
            }

            var visitor = new ExpressionReplacer(expression.Parameters[0], parameter);
            return visitor.Visit(expression.Body);
        }

        public override Expression? Visit(Expression? node) { return node == _oldValue ? _newValue : base.Visit(node); }
    }

    #endregion
}
