using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
/// Represents a decimal number literal value in the filter grammar.
/// </summary>
public class Number : IValue
{
    public Number(TextPosition position, decimal value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    /// Gets the decimal value.
    /// </summary>
    public decimal Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) { return Expression.Constant(Value); }

    #endregion

    public override string ToString() { return Value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
}
