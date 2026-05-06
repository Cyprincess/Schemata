using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
///     The less-than operator (<c>&lt;</c>).
/// </summary>
public class LessThan : IBinary
{
    /// <summary>
    ///     The operator character.
    /// </summary>
    public const char Char = '<';

    /// <summary>
    ///     Initializes a new less-than operator.
    /// </summary>
    public LessThan(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public ExpressionType? Type => ExpressionType.LessThan;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    public override string ToString() { return $"{Char}"; }
}
