namespace Schemata.Modeling.Generator.Expressions;

internal sealed record BooleanLiteral(bool Value) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is BooleanLiteral o && Equals(o); }

    #endregion
}
