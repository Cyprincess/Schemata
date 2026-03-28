namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Reference(string QualifiedName) : IExpression
{
    public bool Equals(IExpression? other) => other is Reference o && Equals(o);
}
