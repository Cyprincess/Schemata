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

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => false;

    /// <inheritdoc />
    public Expression? ToExpression(Container ctx) { return null; }

    /// <inheritdoc />
    public ExpressionType? Type => ExpressionType.LessThanOrEqual;

    /// <inheritdoc />
    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return $"{Name}"; }
}
