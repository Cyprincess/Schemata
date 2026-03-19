using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

public interface IBinary : IToken
{
    ExpressionType? Type { get; }
    Expression?     ToExpression(Expression left, Expression right, Container ctx);
}
