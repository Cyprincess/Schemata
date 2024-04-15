using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public abstract class Match : IBinary
{
    #region IBinary Members

    public abstract TextPosition Position { get; }

    public virtual bool IsConstant => false;

    public virtual Expression? ToExpression(Container ctx) {
        return null;
    }

    public Expression ToExpression(Expression left, Expression right, Container ctx) {
        var method = this switch {
            ExactMatch  => ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]),
            FuzzyMatch  => ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]),
            PrefixMatch => ctx.GetMethod(typeof(string), nameof(string.StartsWith), [typeof(string)]),
            SuffixMatch => ctx.GetMethod(typeof(string), nameof(string.EndsWith), [typeof(string)]),
            var _       => null,
        };

        if (method is null) {
            throw new ParseException("No match method found", Position);
        }

        if (right.Type != typeof(string)) {
            right = Expression.Call(right, "ToString", null);
        }

        if (this is not FuzzyMatch) {
            return Expression.Call(left, method, right);
        }

        var normalize = ctx.GetMethod(typeof(string), nameof(string.ToUpper), []);

        if (normalize is null) {
            return Expression.Call(left, method, right);
        }

        left  = Expression.Call(left, normalize, null);
        right = Expression.Call(right, normalize, null);

        return Expression.Call(left, method, right);
    }

    public virtual ExpressionType? Type => null;

    #endregion
}
