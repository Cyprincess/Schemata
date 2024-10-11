using System.Linq.Expressions;

// ReSharper disable once CheckNamespace
namespace System.Linq;

public static class Predicate
{
    public static Expression<Func<T, bool>> True<T>() {
        return q => true;
    }

    public static Expression<Func<T, bool>> False<T>() {
        return q => false;
    }

    public static Expression<Func<TResult, bool>>? Cast<T, TResult>(Expression<Func<T, bool>>? predicate) {
        if (predicate is null) {
            return null;
        }

        var parameter = Expression.Parameter(typeof(T));

        var body = ExpressionReplacer.Replace(predicate, parameter);

        return Expression.Lambda<Func<TResult, bool>>(body!, parameter);
    }

    public static Expression<Func<TEntity, bool>> And<TEntity>(
        this Expression<Func<TEntity, bool>>? left,
        Expression<Func<TEntity, bool>>?      right) {
        return Combine(left, right, ExpressionType.AndAlso);
    }

    public static Expression<Func<TEntity, bool>> Or<TEntity>(
        this Expression<Func<TEntity, bool>>? left,
        Expression<Func<TEntity, bool>>?      right) {
        return Combine(left, right, ExpressionType.OrElse);
    }

    private static Expression<Func<T, bool>> Combine<T>(
        this Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>>?      right,
        ExpressionType                  type) {
        if (left is null && right is null) {
            return False<T>();
        }

        if (left is null) {
            return right!;
        }

        if (right is null) {
            return left!;
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

        public override Expression? Visit(Expression? node) {
            return node == _oldValue ? _newValue : base.Visit(node);
        }
    }

    #endregion
}
