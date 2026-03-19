namespace Schemata.Modeling.Generator.Expressions;

internal sealed record FunctionCall(string Name, EquatableArray<IExpression> Arguments) : IExpression
{
    #region IExpression Members

    public bool Equals(IExpression? other) { return other is FunctionCall o && Equals(o); }

    #endregion
}
