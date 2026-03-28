namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Represents a literal value token (text, number, boolean, or null) in the filter grammar.
/// </summary>
public interface IValue : IField
{
    /// <summary>
    ///     Gets the raw value of this token.
    /// </summary>
    object? Value { get; }
}
