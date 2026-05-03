namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     A literal value token (text, number, boolean, or null) in the filter grammar.
/// </summary>
public interface IValue : IField
{
    /// <summary>
    ///     Gets the raw value.
    /// </summary>
    object? Value { get; }
}
