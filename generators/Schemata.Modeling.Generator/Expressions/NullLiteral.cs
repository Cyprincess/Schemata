namespace Schemata.Modeling.Generator.Expressions;

internal sealed record NullLiteral : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is NullLiteral; }

    #endregion
}
