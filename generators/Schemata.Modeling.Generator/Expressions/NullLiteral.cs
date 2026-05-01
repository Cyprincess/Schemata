namespace Schemata.Modeling.Generator.Expressions;

public sealed record NullLiteral : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is NullLiteral; }

    #endregion
}
