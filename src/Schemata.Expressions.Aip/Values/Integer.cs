using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

/// <summary>
///     Represents an AIP integer literal.
/// </summary>
public class Integer : IValue
{
    /// <summary>
    ///     Creates an integer literal at the supplied source position.
    /// </summary>
    public Integer(TextPosition position, long value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the parsed integer value.
    /// </summary>
    public long Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value.ToString(); }
}
