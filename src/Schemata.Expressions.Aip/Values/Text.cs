using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Values;

public class Text : IValue
{
    public Text(TextPosition position, string value, bool isQuoted = false) {
        Value    = value;
        Position = position;
        IsQuoted = isQuoted;
    }

    public string Value { get; }

    public bool IsQuoted { get; }

    #region IValue Members

    object IValue.      Value      => Value;
    public TextPosition Position   { get; }
    public bool         IsConstant => true;

    #endregion

    public override string ToString() { return $"\"{Value}\""; }
}
