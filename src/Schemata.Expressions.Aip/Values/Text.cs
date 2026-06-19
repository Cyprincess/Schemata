using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

/// <summary>
///     Represents an AIP text literal or identifier segment.
/// </summary>
public class Text : IValue
{
    /// <summary>
    ///     Creates a text token at the supplied source position.
    /// </summary>
    public Text(TextPosition position, string value, bool isQuoted = false) {
        Value    = value;
        Position = position;
        IsQuoted = isQuoted;
    }

    /// <summary>
    ///     Gets the parsed text value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    ///     Gets a value indicating whether the token came from a quoted literal.
    /// </summary>
    public bool IsQuoted { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return $"\"{Value}\""; }
}
