using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Terms;

public interface IToken
{
    TextPosition Position { get; }

    bool IsConstant { get; }

    Expression? ToExpression(Container ctx);
}
