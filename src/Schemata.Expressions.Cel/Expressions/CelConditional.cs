namespace Schemata.Expressions.Cel.Expressions;

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