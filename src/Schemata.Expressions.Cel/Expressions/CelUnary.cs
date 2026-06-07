namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelUnary : CelNode
{
    public CelUnary(string op, CelNode operand) {
        Operator = op;
        Operand  = operand;
    }

    public string Operator { get; }

    public CelNode Operand { get; }
}