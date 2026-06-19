using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

/// <summary>
///     Represents an AIP boolean literal.
/// </summary>
public class Truth : IValue
{
    /// <summary>
    ///     Creates a boolean literal at the supplied source position.
    /// </summary>
    public Truth(TextPosition position, bool value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the parsed boolean value.
    /// </summary>
    public bool Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value ? "\u2611" : "\u2612"; }
}
