using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelCall : CelNode
{
    public CelCall(string name, IReadOnlyList<CelNode> args) {
        Name = name;
        Args = args;
    }

    public string Name { get; }

    public IReadOnlyList<CelNode> Args { get; }
}