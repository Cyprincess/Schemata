using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Values;

public class Truth : IValue
{
    public Truth(TextPosition position, bool value) {
        Value    = value;
        Position = position;
    }

    public bool Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) {
        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() {
        return Value ? "\u2611" : "\u2612";
    }
}
