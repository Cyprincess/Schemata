using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public class LessThan(TextPosition position) : IBinary
{
    public const char Char = '<';

    #region IBinary Members

    public TextPosition Position { get; } = position;

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) {
        return null;
    }

    public ExpressionType? Type => ExpressionType.LessThan;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        return null;
    }

    #endregion

    public override string ToString() {
        return $"{Char}";
    }
}
