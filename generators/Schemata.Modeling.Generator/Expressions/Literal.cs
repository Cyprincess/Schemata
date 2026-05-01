namespace Schemata.Modeling.Generator.Expressions;

public sealed record Literal(string Value) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is Literal o && Equals(o); }

    #endregion
}
