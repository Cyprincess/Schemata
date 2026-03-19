namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Literal(string Value) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is Literal o && Equals(o); }

    #endregion
}
