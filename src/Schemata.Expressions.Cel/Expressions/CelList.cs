using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelList : CelNode
{
    public CelList(IReadOnlyList<CelNode> items) { Items = items; }

    public IReadOnlyList<CelNode> Items { get; }
}