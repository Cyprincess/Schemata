using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Terms;

namespace Schemata.Resource.Foundation.Filters.Values;

public class Number : IValue
{
    public Number(TextPosition position, decimal value) {
        Position = position;
        Value    = value;
    }

    public decimal Value { get; }

    #region IValue Members

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression? ToExpression(Container ctx) {
        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() {
        return Value.ToString("F");
    }
}
