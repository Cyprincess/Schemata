using System.Collections.Generic;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel.Expressions;

public abstract class CelNode : IExpressionTree
{
    #region IExpressionTree Members

    public string Language => CelLanguage.Name;

    #endregion
}

public sealed class CelConstant : CelNode
{
    public CelConstant(object? value) { Value = value; }

    public object? Value { get; }
}

public sealed class CelIdentifier : CelNode
{
    public CelIdentifier(string name) { Name = name; }

    public string Name { get; }
}

public sealed class CelMember : CelNode
{
    public CelMember(CelNode target, string member) {
        Target = target;
        Member = member;
    }

    public CelNode Target { get; }

    public string Member { get; }
}

public sealed class CelUnary : CelNode
{
    public CelUnary(string op, CelNode operand) {
        Operator = op;
        Operand  = operand;
    }

    public string Operator { get; }

    public CelNode Operand { get; }
}

public sealed class CelBinary : CelNode
{
    public CelBinary(string op, CelNode left, CelNode right) {
        Operator = op;
        Left     = left;
        Right    = right;
    }

    public string Operator { get; }

    public CelNode Left { get; }

    public CelNode Right { get; }
}

public sealed class CelCall : CelNode
{
    public CelCall(string name, IReadOnlyList<CelNode> args) {
        Name = name;
        Args = args;
    }

    public string Name { get; }

    public IReadOnlyList<CelNode> Args { get; }
}

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

public sealed class CelConditional : CelNode
{
    public CelConditional(CelNode condition, CelNode whenTrue, CelNode whenFalse) {
        Condition = condition;
        WhenTrue  = whenTrue;
        WhenFalse = whenFalse;
    }

    public CelNode Condition { get; }

    public CelNode WhenTrue { get; }

    public CelNode WhenFalse { get; }
}

public sealed class CelList : CelNode
{
    public CelList(IReadOnlyList<CelNode> items) { Items = items; }

    public IReadOnlyList<CelNode> Items { get; }
}

public sealed class CelMap : CelNode
{
    public CelMap(IReadOnlyList<KeyValuePair<CelNode, CelNode>> entries) { Entries = entries; }

    public IReadOnlyList<KeyValuePair<CelNode, CelNode>> Entries { get; }
}
