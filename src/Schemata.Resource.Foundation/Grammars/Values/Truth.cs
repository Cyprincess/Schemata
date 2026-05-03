using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
///     A boolean literal (<c>TRUE</c> / <c>FALSE</c>).
/// </summary>
public class Truth : IValue
{
    /// <summary>
    ///     Initializes a new boolean literal.
    /// </summary>
    public Truth(TextPosition position, bool value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the boolean value.
    /// </summary>
    public bool Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => true;

    /// <inheritdoc />
    public Expression ToExpression(Container ctx) { return Expression.Constant(Value); }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return Value ? "\u2611" : "\u2612"; }
}
