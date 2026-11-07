using System;
using Schemata.Quota.Skeleton;

namespace Schemata.Quota.Foundation.Algorithms;

public sealed class TokenBucketQuotaAlgorithm : IQuotaAlgorithm
{
    public QuotaDecision Evaluate(QuotaAlgorithmContext context) {
        var (available, start) = Refill(context.Account.Available, context.Account.WindowStart, context.Policy, context.Now);
        if (available < context.Requested) {
            return new(false, 0, 0, available, start, Retry(context.Requested - available, context.Policy), []);
        }

        return new(true, 0, 0, available - context.Requested, start, null, []);
    }

    public QuotaAccountTransition Settle(QuotaSettlementContext context) {
        var (available, start) = Refill(context.Account.Available, context.Account.WindowStart, context.Policy, context.Now);
        if (context.Kind != QuotaSettlementKind.Commit) available = Math.Min(context.Policy.Burst, available + context.Amount);
        return new(0, 0, available, start, []);
    }

    private static (decimal Available, DateTimeOffset Start) Refill(
        decimal available,
        DateTimeOffset start,
        QuotaPolicy policy,
        DateTimeOffset now
    ) {
        var elapsed = now - start;
        if (elapsed < policy.Period) return (Math.Min(policy.Burst, available), start);
        var periods = elapsed.Ticks / policy.Period.Ticks;
        available = Math.Min(policy.Burst, available + (decimal)periods * policy.Limit);
        return (available, start.AddTicks(checked(periods * policy.Period.Ticks)));
    }

    private static TimeSpan Retry(decimal deficit, QuotaPolicy policy) {
        var periods = decimal.Ceiling(deficit / policy.Limit);
        return periods > long.MaxValue / policy.Period.Ticks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)periods * policy.Period.Ticks);
    }
}
