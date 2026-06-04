using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

public class Integer : IValue
{
    public Integer(TextPosition position, long value) {
        Value    = value;
        Position = position;
    }

    public long Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value.ToString(); }
}
