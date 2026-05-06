using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
///     A text/string literal or identifier. During expression building,
///     the text value is first resolved against bound parameters and expressions
///     in the <see cref="Container" /> before falling back to a constant.
/// </summary>
public class Text : IValue
{
    /// <summary>
    ///     Initializes a new text literal.
    /// </summary>
    /// <param name="position">The position in the source text.</param>
    /// <param name="value">The text value.</param>
    public Text(TextPosition position, string value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the text value.
    /// </summary>
    public string Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression? ToExpression(Container ctx) {
        if (ctx.TryGetParameter(Value, out var value)) {
            return value;
        }

        if (ctx.TryGetExpression(Value, out var expression)) {
            return expression;
        }

        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() { return $"\"{Value}\""; }
}
