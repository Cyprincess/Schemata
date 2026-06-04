using System.Collections.Generic;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

public abstract class LogicalBase : IToken
{
    public abstract IEnumerable<IToken> Tokens { get; }

    public abstract ExpressionType Operator { get; }

    #region IToken Members

    public abstract TextPosition Position { get; }

    public abstract bool IsConstant { get; }

    #endregion
}
