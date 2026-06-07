using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelMemberCall : CelNode
{
    public CelMemberCall(CelNode target, string name, IReadOnlyList<CelNode> args) {
        Target = target;
        Name   = name;
        Args   = args;
    }

    public CelNode Target { get; }

    public string Name { get; }

    public IReadOnlyList<CelNode> Args { get; }
}