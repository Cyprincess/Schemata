namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelIndex : CelNode
{
    public CelIndex(CelNode target, CelNode index) {
        Target = target;
        Index  = index;
    }

    public CelNode Target { get; }

    public CelNode Index { get; }
}
