using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Terms;

namespace Schemata.Resource.Foundation.Filters.Values;

public class Integer : IValue
{
    public Integer(TextPosition position, long value) {
        Position = position;
        Value    = value;
    }

    public long Value { get; }

    #region IValue Members

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression? ToExpression(Container ctx) {
        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() {
        return Value.ToString();
    }
}
