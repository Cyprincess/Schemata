using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
/// Represents the less-than-or-equal operator (<c>&lt;=</c>).
/// </summary>
public class LessThanOrEqual : IBinary
{
    /// <summary>
    /// The string literal representing the less-than-or-equal operator.
    /// </summary>
    public const string Name = "<=";

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
