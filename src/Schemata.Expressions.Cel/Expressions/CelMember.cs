namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelMember : CelNode
{
    public CelMember(CelNode target, string member) {
        Target = target;
        Member = member;
    }

    public CelNode Target { get; }

    public string Member { get; }
}