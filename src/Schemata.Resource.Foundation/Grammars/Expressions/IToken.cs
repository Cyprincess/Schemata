using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
/// Base interface for all filter grammar tokens that can produce LINQ expressions.
/// </summary>
public interface IToken
{
    /// <summary>
    /// Gets the position of this token in the source text.
    /// </summary>
    TextPosition Position { get; }

    /// <summary>
    /// Gets whether this token represents a constant value.
    /// </summary>
    bool IsConstant { get; }

    /// <summary>
    /// Converts this token to a LINQ expression using the specified container for bindings.
    /// </summary>
    /// <param name="ctx">The expression-building container with parameter and function bindings.</param>
    /// <returns>The LINQ expression, or <see langword="null"/> if the token cannot be converted.</returns>
    Expression? ToExpression(Container ctx);
}
