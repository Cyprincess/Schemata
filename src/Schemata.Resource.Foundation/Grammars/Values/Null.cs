using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Values;

public class Null(TextPosition position) : IValue
{
    #region IValue Members

    object? IValue.Value => null;

    public TextPosition Position { get; } = position;

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) {
        return Expression.Constant(null);
    }

    #endregion

    public override string ToString() {
        return "\u2205";
    }
}
