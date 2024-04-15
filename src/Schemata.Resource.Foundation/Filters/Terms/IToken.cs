using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Filters.Terms;

public interface IToken
{
    TextPosition Position { get; }

    bool IsConstant { get; }

    Expression? ToExpression(Container ctx);
}
