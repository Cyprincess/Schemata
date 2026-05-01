namespace Schemata.Modeling.Generator.Expressions;

public sealed record FunctionCall(string Name, EquatableArray<IExpression> Arguments) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is FunctionCall o && Equals(o); }

    #endregion
}
