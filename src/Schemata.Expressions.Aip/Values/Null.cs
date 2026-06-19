using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

/// <summary>
///     Represents an AIP null literal.
/// </summary>
public class Null : IValue
{
    /// <summary>
    ///     Creates a null literal at the supplied source position.
    /// </summary>
    public Null(TextPosition position) { Position = position; }

    #region IValue Members

    object? IValue.     Value      => null;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return "\u2205"; }
}
