using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
///     The greater-than operator (<c>&gt;</c>).
/// </summary>
public class GreaterThan : IBinary
{
    /// <summary>
    ///     The operator character.
    /// </summary>
    public const char Char = '>';

    /// <summary>
    ///     Initializes a new greater-than operator.
    /// </summary>
    public GreaterThan(TextPosition position) { Position = position; }

    #region IBinary Members

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => false;

    /// <inheritdoc />
    public Expression? ToExpression(Container ctx) { return null; }

    /// <inheritdoc />
    public ExpressionType? Type => ExpressionType.GreaterThan;

    /// <inheritdoc />
    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return $"{Char}"; }
}
