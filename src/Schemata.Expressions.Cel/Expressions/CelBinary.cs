namespace Schemata.Expressions.Cel.Expressions;

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