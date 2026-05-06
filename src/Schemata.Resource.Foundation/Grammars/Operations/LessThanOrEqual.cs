using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
///     The less-than-or-equal operator (<c>&lt;=</c>).
/// </summary>
public class LessThanOrEqual : IBinary
{
    /// <summary>
    ///     The operator string.
    /// </summary>
    public const string Name = "<=";

    /// <summary>
    ///     Initializes a new less-than-or-equal operator.
    /// </summary>
    public LessThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public ExpressionType? Type => ExpressionType.LessThanOrEqual;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    public override string ToString() { return $"{Name}"; }
}
