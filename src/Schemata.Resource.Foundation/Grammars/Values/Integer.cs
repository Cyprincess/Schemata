using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Values;

public class Integer(TextPosition position, long value) : IValue
{
    public long Value { get; } = value;

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; } = position;

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) {
        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() {
        return Value.ToString();
    }
}
