using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Values;

public class Null : IValue
{
    public Null(TextPosition position) {
        Position = position;
    }

    #region IValue Members

    object? IValue.Value => null;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression? ToExpression(Container ctx) {
        return Expression.Constant(null);
    }

    #endregion

    public override string ToString() {
        return "\u2205";
    }
}
