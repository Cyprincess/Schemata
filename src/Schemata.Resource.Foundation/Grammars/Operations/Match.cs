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

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        var normalize = ctx.GetMethod(typeof(string), nameof(string.ToUpper), []);

        if (normalize is null) {
            return null;
        }

        var method = this switch {
            ExactMatch  => ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]),
            FuzzyMatch  => ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]),
            PrefixMatch => ctx.GetMethod(typeof(string), nameof(string.StartsWith), [typeof(string)]),
            SuffixMatch => ctx.GetMethod(typeof(string), nameof(string.EndsWith), [typeof(string)]),
            var _       => null,
        };

        if (method is null) {
            return null;
        }

        if (right.Type != typeof(string)) {
            right = Expression.Call(right, "ToString", null);
        }

        return Expression.Call(Expression.Call(left, normalize, null), method, Expression.Call(right, normalize, null));
    }

    public virtual ExpressionType? Type => null;

    #endregion
}
