using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Filters.Terms;

public interface IBinary : IToken
{
    ExpressionType? Type { get; }
    Expression?     ToExpression(Expression left, Expression right, Container ctx);
}
