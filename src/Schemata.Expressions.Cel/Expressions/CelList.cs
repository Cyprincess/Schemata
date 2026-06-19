using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL list literal.
/// </summary>
public sealed class CelList : CelNode
{
    /// <summary>
    ///     Creates a list literal from item nodes.
    /// </summary>
    public CelList(IReadOnlyList<CelNode> items) { Items = items; }

    /// <summary>
    ///     Gets the list item expressions.
    /// </summary>
    public IReadOnlyList<CelNode> Items { get; }
}
