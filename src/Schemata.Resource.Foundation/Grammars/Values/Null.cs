using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
///     A null literal (<c>NULL</c>).
/// </summary>
public class Null : IValue
{
    /// <summary>
    ///     Initializes a new null literal.
    /// </summary>
    public Null(TextPosition position) { Position = position; }

    #region IValue Members

    object? IValue.Value => null;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) { return Expression.Constant(null); }

    #endregion

    public override string ToString() { return "\u2205"; }
}
