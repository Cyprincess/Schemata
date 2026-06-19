namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL literal value.
/// </summary>
public sealed class CelConstant : CelNode
{
    /// <summary>
    ///     Creates a literal value node.
    /// </summary>
    public CelConstant(object? value) { Value = value; }

    /// <summary>
    ///     Gets the literal CLR value.
    /// </summary>
    public object? Value { get; }
}
