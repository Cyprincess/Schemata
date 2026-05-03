using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     An optionally negated simple expression (prefix <c>NOT</c> or <c>-</c>).
///     On constant children, negation is applied at compile-time. On non-constant
///     children, <see cref="Expression.Not(Expression)" /> or <see cref="Expression.Negate(Expression)" /> is used.
/// </summary>
public class Term : IToken
{
    /// <summary>
    ///     Initializes a new term with an optional unary modifier.
    /// </summary>
    /// <param name="position">The position in the source text.</param>
    /// <param name="unary">The unary modifier string, or <see langword="null" />.</param>
    /// <param name="simple">The inner simple expression.</param>
    public Term(TextPosition position, string? unary, ISimple simple) {
        Modifier = unary;
        Simple   = simple;
        Position = position;
    }

    /// <summary>
    ///     Gets the unary modifier (<c>"NOT"</c> or <c>"-"</c>), or <see langword="null" />.
    /// </summary>
    public string? Modifier { get; }

    /// <summary>
    ///     Gets the inner simple expression.
    /// </summary>
    public ISimple Simple { get; }

    #region IToken Members

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => Simple.IsConstant;

    /// <inheritdoc />
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

        return expression.Type == typeof(bool) ? Expression.Not(expression) : Expression.Negate(expression);
    }

    #endregion

    /// <inheritdoc />
    public override string? ToString() { return Modifier is not null ? $"{Modifier} {Simple}" : Simple.ToString(); }
}
