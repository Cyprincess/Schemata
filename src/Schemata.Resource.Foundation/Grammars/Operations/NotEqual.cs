using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
///     The not-equal operator (<c>!=</c>).
/// </summary>
public class NotEqual : IBinary
{
    /// <summary>
    ///     The operator string.
    /// </summary>
    public const string Name = "!=";

    /// <summary>
    ///     Initializes a new not-equal operator.
    /// </summary>
    public NotEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => false;

    /// <inheritdoc />
    public Expression? ToExpression(Container ctx) { return null; }

    /// <inheritdoc />
    public ExpressionType? Type => ExpressionType.NotEqual;

    /// <inheritdoc />
    public Expression? ToExpression(Expression left, Expression right, Container ctx) { return null; }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return $"{Name}"; }
}
