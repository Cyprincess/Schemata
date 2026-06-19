using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents a parsed AIP syntax token.
/// </summary>
public interface IToken
{
    /// <summary>
    ///     Gets the token position in the source text.
    /// </summary>
    TextPosition Position { get; }

    /// <summary>
    ///     Gets a value indicating whether the token is independent of the evaluation context.
    /// </summary>
    bool IsConstant { get; }
}
