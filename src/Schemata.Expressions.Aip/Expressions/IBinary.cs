using System.Linq.Expressions;

namespace Schemata.Expressions.Aip.Expressions;

public interface IBinary : IToken
{
    ExpressionType? Type { get; }
}
