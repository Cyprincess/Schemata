using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

public interface IToken
{
    TextPosition Position { get; }

    bool IsConstant { get; }

    Expression? ToExpression(Container ctx);
}
