using System.Globalization;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

/// <summary>
///     Represents an AIP decimal number literal.
/// </summary>
public class Number : IValue
{
    /// <summary>
    ///     Creates a decimal number literal at the supplied source position.
    /// </summary>
    public Number(TextPosition position, decimal value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the parsed decimal value.
    /// </summary>
    public decimal Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value.ToString(CultureInfo.InvariantCulture); }
}
