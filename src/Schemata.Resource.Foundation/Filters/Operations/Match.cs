using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Terms;

namespace Schemata.Resource.Foundation.Filters.Operations;

public abstract class Match : IBinary
{
    #region IBinary Members

    public abstract TextPosition Position { get; }

    public virtual bool IsConstant => false;

    public virtual Expression? ToExpression(Container ctx) {
        return null;
    }

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        var method = this switch {
            ExactMatch  => ctx.GetMethod(typeof(string), "Contains", [typeof(string)]),
            FuzzyMatch  => ctx.GetMethod(typeof(string), "Contains", [typeof(string)]),
            PrefixMatch => ctx.GetMethod(typeof(string), "StartsWith", [typeof(string)]),
            SuffixMatch => ctx.GetMethod(typeof(string), "EndsWith", [typeof(string)]),
            var _       => null,
        };

        if (method is null) {
            return null;
        }

        if (right.Type != typeof(string)) {
            right = Expression.Call(right, "ToString", null);
        }

        return Expression.Call(left, method, right);
    }

    public virtual ExpressionType? Type => null;

    #endregion
}
