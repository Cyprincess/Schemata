using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

public class Truth : IValue
{
    public Truth(TextPosition position, bool value) {
        Value    = value;
        Position = position;
    }

    public bool Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value ? "\u2611" : "\u2612"; }
}
