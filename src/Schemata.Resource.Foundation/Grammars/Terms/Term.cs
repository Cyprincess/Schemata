using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Terms;

public class Term : IToken
{
    public Term(TextPosition position, string? unary, ISimple simple) {
        Modifier = unary;
        Simple   = simple;
        Position = position;
    }

    public string? Modifier { get; }

    public ISimple Simple { get; }

    #region IToken Members

    public TextPosition Position { get; }

    public bool IsConstant => Simple.IsConstant;

    public Expression? ToExpression(Container ctx) {
        var expression = Simple.ToExpression(ctx);

        if (Modifier is null) {
            return expression;
        }

        if (expression is null) {
            throw new ParseException("Except simple", Simple.Position);
        }

        if (Simple.IsConstant && expression is ConstantExpression constant) {
            return Modifier switch {
                "-" or "NOT" when constant.Value is bool b    => Expression.Constant(!b),
                "-" or "NOT" when constant.Value is long i    => Expression.Constant(-i),
                "-" or "NOT" when constant.Value is decimal n => Expression.Constant(-n),
                var _                                         => null,
            };
        }

        return Expression.Negate(expression);
    }

    #endregion

    public override string? ToString() {
        return Modifier is not null ? $"{Modifier} {Simple}" : Simple.ToString();
    }
}
