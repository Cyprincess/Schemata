using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
///     The greater-than-or-equal operator (<c>&gt;=</c>).
/// </summary>
public class GreaterThanOrEqual : IBinary
{
    /// <summary>
    ///     The operator string.
    /// </summary>
    public const string Name = ">=";

    /// <summary>
    ///     Initializes a new greater-than-or-equal operator.
    /// </summary>
    public GreaterThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => false;

    /// <inheritdoc />
    public Expression? ToExpression(Container ctx) { return null; }

    /// <inheritdoc />
    public ExpressionType? Type => ExpressionType.GreaterThanOrEqual;

    /// <inheritdoc />
    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return $"{Name}"; }
}
