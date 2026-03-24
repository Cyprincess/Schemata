using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
/// Represents the greater-than-or-equal operator (<c>&gt;=</c>).
/// </summary>
public class GreaterThanOrEqual : IBinary
{
    /// <summary>
    /// The string literal representing the greater-than-or-equal operator.
    /// </summary>
    public const string Name = ">=";

    public GreaterThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public ExpressionType? Type => ExpressionType.GreaterThanOrEqual;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    public override string ToString() { return $"{Name}"; }
}
