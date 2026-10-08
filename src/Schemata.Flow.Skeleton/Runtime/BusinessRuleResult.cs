namespace Schemata.Flow.Skeleton.Runtime;

public sealed record BusinessRuleResult<TResult>(bool Matched, TResult? Output = default);
