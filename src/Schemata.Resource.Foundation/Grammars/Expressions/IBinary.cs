using System.Linq.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
/// Represents a binary comparator operator (=, !=, &lt;, &gt;, &lt;=, &gt;=, :) in the filter grammar.
/// </summary>
public interface IBinary : IToken
{
    /// <summary>
    /// Gets the LINQ expression type for simple comparisons, or <see langword="null"/> for custom operators like <c>=</c> and <c>:</c>.
    /// </summary>
    ExpressionType? Type { get; }

    /// <summary>
    /// Builds a binary expression from the left and right operands.
    /// </summary>
    /// <param name="left">The left-hand expression.</param>
    /// <param name="right">The right-hand expression.</param>
    /// <param name="ctx">The expression-building container.</param>
    /// <returns>The combined binary expression.</returns>
    Expression? ToExpression(Expression left, Expression right, Container ctx);
}
