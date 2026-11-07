using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Foundation.Algorithms;

public sealed class SlidingWindowQuotaAlgorithm : IQuotaAlgorithm
{
    public QuotaDecision Evaluate(QuotaAlgorithmContext context) {
        var segments = Live(context.Segments, context.Now);
        var committed = Sum(segments, segment => segment.Committed);
        var reserved = Sum(segments, segment => segment.Reserved);
        var total = (decimal)committed + reserved + context.Requested;
        if (total > context.Policy.Limit) {
            return new(false, committed, reserved, QuotaArithmetic.Available(context.Policy.Limit, committed, reserved),
                context.Now, Retry(segments, total - context.Policy.Limit, context.Policy.Period, context.Now), segments);
        }

        var updated = new List<SchemataQuotaSegment>(segments) {
            new() {
                Uid = context.AllocationUid, AccountUid = context.Account.Uid, StartTime = context.Now,
                ExpireTime = context.Now.Add(context.Policy.Period), Reserved = context.Requested,
            },
        };
        return new(true, committed, checked(reserved + context.Requested), context.Policy.Limit - total,
            context.Now, null, updated);
    }

    public QuotaAccountTransition Settle(QuotaSettlementContext context) {
        var segments = Live(context.Segments, context.Now);
        var segment = segments.Find(row => row.Uid == context.AllocationUid);
        if (segment is not null) {
            var amount = Math.Min(context.Amount, segment.Reserved);
            segment.Reserved -= amount;
            if (context.Kind == QuotaSettlementKind.Commit) segment.Committed = checked(segment.Committed + amount);
        }

        var committed = Sum(segments, row => row.Committed);
        var reserved = Sum(segments, row => row.Reserved);
        return new(committed, reserved, QuotaArithmetic.Available(context.Policy.Limit, committed, reserved), context.Now, segments);
    }

    private static List<SchemataQuotaSegment> Live(IReadOnlyList<SchemataQuotaSegment> segments, DateTimeOffset now) {
        return segments.Where(segment => segment.ExpireTime > now)
                       .Select(segment => new SchemataQuotaSegment {
                           Uid = segment.Uid, AccountUid = segment.AccountUid, StartTime = segment.StartTime,
                           ExpireTime = segment.ExpireTime, Committed = segment.Committed, Reserved = segment.Reserved,
                       }).ToList();
    }

    private static long Sum(IEnumerable<SchemataQuotaSegment> segments, Func<SchemataQuotaSegment, long> select) {
        var total = 0L;
        foreach (var segment in segments) total = checked(total + select(segment));
        return total;
    }

    private static TimeSpan Retry(
        IEnumerable<SchemataQuotaSegment> segments,
        decimal deficit,
        TimeSpan period,
        DateTimeOffset now
    ) {
        var released = 0m;
        foreach (var segment in segments.OrderBy(segment => segment.ExpireTime)) {
            released += (decimal)segment.Committed + segment.Reserved;
            if (released < deficit) continue;
            var retry = segment.ExpireTime - now;
            return retry < TimeSpan.Zero ? TimeSpan.Zero : retry;
        }

        return period;
    }
}
