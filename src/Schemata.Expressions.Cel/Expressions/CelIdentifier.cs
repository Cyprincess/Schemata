namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL identifier reference.
/// </summary>
public sealed class CelIdentifier : CelNode
{
    /// <summary>
    ///     Creates an identifier reference node.
    /// </summary>
    public CelIdentifier(string name) { Name = name; }

    /// <summary>
    ///     Gets the identifier name.
    /// </summary>
    public string Name { get; }
}
