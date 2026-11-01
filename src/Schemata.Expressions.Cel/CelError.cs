using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

public sealed record CelError(string Message) : IExpressionError
{
    string IExpressionError.Reason => "CEL_EVALUATION_ERROR";
}
