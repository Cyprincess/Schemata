using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Values;

public class Text(TextPosition position, string value) : IValue
{
    public string Value { get; } = value;

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; } = position;

    public bool IsConstant => true;

    public Expression? ToExpression(Container ctx) {
        if (ctx.TryGetParameter(Value, out var value)) {
            return value;
        }

        if (ctx.TryGetExpression(Value, out var expression)) {
            return expression;
        }

        return Expression.Constant(Value);
    }

    #endregion

    public override string ToString() {
        return $"\"{Value}\"";
    }
}
