using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
/// Represents the greater-than operator (<c>&gt;</c>).
/// </summary>
public class GreaterThan : IBinary
{
    /// <summary>
    /// The character representing the greater-than operator.
    /// </summary>
    public const char Char = '>';

    public GreaterThan(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public ExpressionType? Type => ExpressionType.GreaterThan;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    public override string ToString() { return $"{Char}"; }
}
