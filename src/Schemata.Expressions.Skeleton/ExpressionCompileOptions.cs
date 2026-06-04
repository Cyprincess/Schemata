using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

public sealed class ExpressionCompileOptions
{
    public IDictionary<string, ExpressionFunction> Functions { get; } = new Dictionary<string, ExpressionFunction>();
}
