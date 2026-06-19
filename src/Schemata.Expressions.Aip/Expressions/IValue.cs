namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP literal or identifier value.
/// </summary>
public interface IValue : IField
{
    /// <summary>
    ///     Gets the parsed CLR value.
    /// </summary>
    object? Value { get; }
}
