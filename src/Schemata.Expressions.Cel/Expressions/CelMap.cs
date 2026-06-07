using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelMap : CelNode
{
    public CelMap(IReadOnlyList<KeyValuePair<CelNode, CelNode>> entries) { Entries = entries; }

    public IReadOnlyList<KeyValuePair<CelNode, CelNode>> Entries { get; }
}