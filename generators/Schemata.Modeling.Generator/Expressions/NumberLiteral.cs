namespace Schemata.Modeling.Generator.Expressions;

internal sealed record NumberLiteral(string Raw) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is NumberLiteral o && Equals(o); }

    #endregion
}
