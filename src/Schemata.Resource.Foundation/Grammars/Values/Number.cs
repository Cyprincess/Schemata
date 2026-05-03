using System.Globalization;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
///     A decimal number literal.
/// </summary>
public class Number : IValue
{
    /// <summary>
    ///     Initializes a new number literal.
    /// </summary>
    public Number(TextPosition position, decimal value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the decimal value.
    /// </summary>
    public decimal Value { get; }

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
    public override string ToString() { return Value.ToString(CultureInfo.InvariantCulture); }
}
