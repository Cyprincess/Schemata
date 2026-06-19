using System.Collections.Generic;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Base type for AIP logical groups that combine child tokens with a boolean operator.
/// </summary>
public abstract class LogicalBase : IToken
{
    /// <summary>
    ///     Gets the child tokens in the logical group.
    /// </summary>
    public abstract IEnumerable<IToken> Tokens { get; }

    /// <summary>
    ///     Gets the expression-tree operator that combines child tokens.
    /// </summary>
    public abstract ExpressionType Operator { get; }

    #region IToken Members

    public abstract TextPosition Position { get; }

    public abstract bool IsConstant { get; }

    #endregion
}
