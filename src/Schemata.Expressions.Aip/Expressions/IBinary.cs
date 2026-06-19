using System.Linq.Expressions;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP binary comparator token.
/// </summary>
public interface IBinary : IToken
{
    /// <summary>
    ///     Gets the LINQ expression type used by this comparator when it maps directly to one.
    /// </summary>
    ExpressionType? Type { get; }
}
