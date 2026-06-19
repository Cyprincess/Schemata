using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL map literal.
/// </summary>
public sealed class CelMap : CelNode
{
    /// <summary>
    ///     Creates a map literal from key-value expression pairs.
    /// </summary>
    public CelMap(IReadOnlyList<KeyValuePair<CelNode, CelNode>> entries) { Entries = entries; }

    /// <summary>
    ///     Gets the map entry expressions.
    /// </summary>
    public IReadOnlyList<KeyValuePair<CelNode, CelNode>> Entries { get; }
}
