using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
/// Represents a text (string) literal or identifier value in the filter grammar.
/// </summary>
/// <remarks>
/// During expression building, text values are first resolved against bound parameters and expressions
/// in the container before falling back to a constant string.
/// </remarks>
public class Text : IValue
{
    public Text(TextPosition position, string value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    /// Gets the text value.
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
