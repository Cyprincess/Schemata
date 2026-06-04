using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

public interface IToken
{
    TextPosition Position { get; }

    bool IsConstant { get; }
}
