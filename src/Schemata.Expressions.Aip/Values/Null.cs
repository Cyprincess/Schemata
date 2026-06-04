using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

public class Null : IValue
{
    public Null(TextPosition position) { Position = position; }

    #region IValue Members

    object? IValue.     Value      => null;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return "\u2205"; }
}
