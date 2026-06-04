using System.Globalization;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

public class Number : IValue
{
    public Number(TextPosition position, decimal value) {
        Value    = value;
        Position = position;
    }

    public decimal Value { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return Value.ToString(CultureInfo.InvariantCulture); }
}
