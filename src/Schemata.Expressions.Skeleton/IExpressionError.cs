namespace Schemata.Expressions.Skeleton;

/// <summary>Identifies a failed evaluation returned as an expression value.</summary>
public interface IExpressionError
{
    string Reason { get; }
    string Message { get; }
}
