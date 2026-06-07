namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelConstant : CelNode
{
    public CelConstant(object? value) { Value = value; }

    public object? Value { get; }
}