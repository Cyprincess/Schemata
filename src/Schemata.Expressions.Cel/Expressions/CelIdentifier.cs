namespace Schemata.Expressions.Cel.Expressions;

public sealed class CelIdentifier : CelNode
{
    public CelIdentifier(string name) { Name = name; }

    public string Name { get; }
}