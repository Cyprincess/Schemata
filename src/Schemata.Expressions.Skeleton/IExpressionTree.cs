namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Represents a parsed expression tree produced by a language parser.
/// </summary>
public interface IExpressionTree
{
    /// <summary>
    ///     Gets the language identifier for the tree.
    /// </summary>
    string Language { get; }
}
