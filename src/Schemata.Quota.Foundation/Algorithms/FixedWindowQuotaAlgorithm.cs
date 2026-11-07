using System;
using System.Collections.Generic;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Foundation.Algorithms;

public sealed class FixedWindowQuotaAlgorithm : IQuotaAlgorithm
{
    public QuotaDecision Evaluate(QuotaAlgorithmContext context) {
        var account = Reset(context.Account, context.Policy, context.Now);
        var retry = Retry(account.WindowStart, context.Policy.Period, context.Now);
        if (!QuotaArithmetic.TryTotal(account.Committed, account.Reserved, context.Requested, context.Policy.Limit, out var total)) {
            return Rejected(account, context.Policy.Limit, retry, context.Segments);
        }

        var reserved = checked(account.Reserved + context.Requested);
        return new(true, account.Committed, reserved, context.Policy.Limit - total, account.WindowStart, null, context.Segments);
    }

    public QuotaAccountTransition Settle(QuotaSettlementContext context) {
        var account = Reset(context.Account, context.Policy, context.Now);
        var committed = account.Committed;
        var reserved = account.Reserved;
        if (context.WindowStart != account.WindowStart) {
            return new(committed, reserved, QuotaArithmetic.Available(context.Policy.Limit, committed, reserved),
                account.WindowStart, context.Segments);
        }

        if (context.Kind == QuotaSettlementKind.Commit) {
            var amount = Math.Min(context.Amount, reserved);
            reserved -= amount;
            committed = checked(committed + amount);
        } else {
            reserved -= Math.Min(context.Amount, reserved);
        }

        var available = QuotaArithmetic.Available(context.Policy.Limit, committed, reserved);
        return new(committed, reserved, available, account.WindowStart, context.Segments);
    }

    private static SchemataQuotaAccount Reset(SchemataQuotaAccount account, QuotaPolicy policy, DateTimeOffset now) {
        var elapsed = now - account.WindowStart;
        if (elapsed < policy.Period) return account;
        var periods = elapsed.Ticks / policy.Period.Ticks;
        return new() {
            Uid = account.Uid, Scope = account.Scope, Subject = account.Subject, Metric = account.Metric,
            PolicyName = account.PolicyName, PolicyVersion = account.PolicyVersion,
            Available = policy.Limit, WindowStart = account.WindowStart.AddTicks(checked(periods * policy.Period.Ticks)),
            Timestamp = account.Timestamp, CreateTime = account.CreateTime, UpdateTime = account.UpdateTime,
        };
    }

    private static TimeSpan Retry(DateTimeOffset start, TimeSpan period, DateTimeOffset now) {
        var retry = start.Add(period) - now;
        return retry < TimeSpan.Zero ? TimeSpan.Zero : retry;
    }

    private static QuotaDecision Rejected(
        SchemataQuotaAccount account,
        long limit,
        TimeSpan retry,
        IReadOnlyList<SchemataQuotaSegment> segments
    ) {
        return new(false, account.Committed, account.Reserved,
            QuotaArithmetic.Available(limit, account.Committed, account.Reserved), account.WindowStart, retry, segments);
    }
}

internal static class QuotaArithmetic
{
    internal static bool TryTotal(long committed, long reserved, long requested, long limit, out long total) {
        total = 0;
        try {
            total = checked(committed + reserved + requested);
            return total <= limit;
        } catch (OverflowException) {
            return false;
        }
    }

    internal static decimal Available(long limit, long committed, long reserved) {
        return Math.Max(0, (decimal)limit - committed - reserved);
    }
}
