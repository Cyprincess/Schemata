using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Base interface for all filter grammar tokens that can produce LINQ expressions.
/// </summary>
public interface IToken
{
    /// <summary>
    ///     Gets the position in the source text.
    /// </summary>
    TextPosition Position { get; }

    /// <summary>
    ///     Gets whether this token is a compile-time constant.
    /// </summary>
    bool IsConstant { get; }

    /// <summary>
    ///     Converts this token to a LINQ expression using bindings from the container.
    /// </summary>
    /// <param name="ctx">The expression-building <see cref="Container" />.</param>
    /// <returns>The LINQ expression, or <see langword="null" />.</returns>
    Expression? ToExpression(Container ctx);
}
