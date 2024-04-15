using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Terms;

namespace Schemata.Resource.Foundation.Filters.Operations;

public class Equal : IBinary
{
    public const char Char = '=';

    public Equal(TextPosition position) {
        Position = position;
    }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) {
        return null;
    }

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        return null;
    }

    public ExpressionType? Type => ExpressionType.Equal;

    #endregion

    public override string ToString() {
        return $"{Char}";
    }
}
