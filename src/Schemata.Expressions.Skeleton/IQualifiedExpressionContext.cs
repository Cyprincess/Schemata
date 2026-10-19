namespace Schemata.Expressions.Skeleton;

/// <summary>Resolves qualified root members independently of bare dynamic row identifiers.</summary>
public interface IQualifiedExpressionContext
{
    bool TryGetQualifiedMember(string qualifier, string member, out object? value);
}
