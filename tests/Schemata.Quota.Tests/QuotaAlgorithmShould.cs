using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Quota.Foundation.Algorithms;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;
using Xunit;

namespace Schemata.Quota.Tests;

public class QuotaAlgorithmShould
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accept_Exact_Fixed_Window_Capacity_And_Reject_One_More() {
        var algorithm = new FixedWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1));

        var exact = algorithm.Evaluate(Context(policy, 10));
        var overflow = algorithm.Evaluate(Context(policy, 1, Account(committed: 10)));

        Assert.True(exact.Allowed);
        Assert.Equal(10, exact.Reserved);
        Assert.Equal(0, exact.Available);
        Assert.False(overflow.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(1), overflow.RetryAfter);
    }

    [Fact]
    public void Apply_Weighted_Fixed_Window_Charges_And_Reset_At_The_Boundary() {
        var algorithm = new FixedWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1));
        var account = Account(committed: 7, reserved: 2, available: 1);
        var previous = Account(committed: 7, reserved: 2, available: 1, windowStart: Now.AddMinutes(-1));

        var weighted = algorithm.Evaluate(Context(policy, 2, account));
        var reset = algorithm.Evaluate(Context(policy, 10, previous, now: Now.AddMinutes(1)));

        Assert.False(weighted.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(1), weighted.RetryAfter);
        Assert.True(reset.Allowed);
        Assert.Equal(0, reset.Committed);
        Assert.Equal(10, reset.Reserved);
        Assert.Equal(Now.AddMinutes(1), reset.WindowStart);
    }

    [Fact]
    public void Move_Fixed_Window_Usage_For_Every_Settlement_Kind() {
        var algorithm = new FixedWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1));
        var account = Account(committed: 2, reserved: 5, available: 3);

        var committed = algorithm.Settle(Settlement(policy, 3, QuotaSettlementKind.Commit, account));
        var released = algorithm.Settle(Settlement(policy, 3, QuotaSettlementKind.Release, account));
        var expired = algorithm.Settle(Settlement(policy, 3, QuotaSettlementKind.Expire, account));

        Assert.Equal((5, 2, 3m), (committed.Committed, committed.Reserved, committed.Available));
        Assert.Equal((2, 2, 6m), (released.Committed, released.Reserved, released.Available));
        Assert.Equal((2, 2, 6m), (expired.Committed, expired.Reserved, expired.Available));
    }

    [Fact]
    public void Drop_Expired_Sliding_Segments_Before_Weighted_Evaluation() {
        var algorithm = new SlidingWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.SlidingWindow, 10, TimeSpan.FromMinutes(1));
        var segments = new[] {
            Segment(6, 2, Now.AddMinutes(-2), Now.AddSeconds(-1)),
            Segment(1, 0, Now.AddSeconds(-30), Now.AddSeconds(30)),
        };
        var account = Account(committed: 7, reserved: 2, available: 1);

        var decision = algorithm.Evaluate(Context(policy, 9, account, segments: segments));

        Assert.True(decision.Allowed);
        Assert.Equal(1, decision.Committed);
        Assert.Equal(9, decision.Reserved);
        Assert.Equal(0, decision.Available);
        Assert.Equal(2, decision.Segments.Count);
        Assert.DoesNotContain(decision.Segments, segment => segment.ExpireTime <= Now);
    }

    [Fact]
    public void Compute_Sliding_Retry_From_The_Oldest_Segment_That_Covers_The_Deficit() {
        var algorithm = new SlidingWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.SlidingWindow, 10, TimeSpan.FromMinutes(1));
        var segments = new[] {
            Segment(4, 0, Now.AddSeconds(-50), Now.AddSeconds(10)),
            Segment(3, 0, Now.AddSeconds(-40), Now.AddSeconds(20)),
            Segment(3, 0, Now.AddSeconds(-30), Now.AddSeconds(30)),
        };

        var decision = algorithm.Evaluate(Context(policy, 6, Account(committed: 10, available: 0), segments: segments));

        Assert.False(decision.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(20), decision.RetryAfter);
    }

    [Fact]
    public void Settle_Only_Live_Sliding_Window_Reservations() {
        var algorithm = new SlidingWindowQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.SlidingWindow, 10, TimeSpan.FromMinutes(1));
        var live = Segment(1, 4, Now.AddSeconds(-20), Now.AddSeconds(40));
        var expired = Segment(2, 3, Now.AddMinutes(-2), Now.AddSeconds(-1));
        var account = Account(committed: 3, reserved: 7, available: 0);

        var committed = algorithm.Settle(Settlement(policy, 4, QuotaSettlementKind.Commit, account, [live, expired]));
        var released = algorithm.Settle(Settlement(policy, 4, QuotaSettlementKind.Release, account, [live, expired]));

        Assert.Equal((5, 0, 5m), (committed.Committed, committed.Reserved, committed.Available));
        Assert.Equal((1, 0, 9m), (released.Committed, released.Reserved, released.Available));
        Assert.Single(committed.Segments);
        Assert.Equal(5, committed.Segments[0].Committed);
    }

    [Fact]
    public void Refill_Token_Bucket_By_Whole_Periods_And_Cap_At_Burst() {
        var algorithm = new TokenBucketQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.TokenBucket, 2, TimeSpan.FromSeconds(10), burst: 5);
        var account = Account(available: 1, windowStart: Now.AddSeconds(-35));

        var decision = algorithm.Evaluate(Context(policy, 5, account));

        Assert.True(decision.Allowed);
        Assert.Equal(0, decision.Available);
        Assert.Equal(Now.AddSeconds(-5), decision.WindowStart);
    }

    [Fact]
    public void Consume_Immediate_Token_Burst_And_Compute_Deficit_Retry() {
        var algorithm = new TokenBucketQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.TokenBucket, 3, TimeSpan.FromSeconds(5), burst: 9);
        var account = Account(available: 9, windowStart: Now);

        var burst = algorithm.Evaluate(Context(policy, 9, account));
        var rejected = algorithm.Evaluate(Context(policy, 8, Account(available: 2, windowStart: Now)));

        Assert.True(burst.Allowed);
        Assert.Equal(0, burst.Available);
        Assert.False(rejected.Allowed);
        Assert.Equal(TimeSpan.FromSeconds(10), rejected.RetryAfter);
    }

    [Fact]
    public void Settle_Tokens_Without_Adding_Period_Consumption() {
        var algorithm = new TokenBucketQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.TokenBucket, 2, TimeSpan.FromSeconds(10), burst: 6);
        var account = Account(available: 2, windowStart: Now);

        var committed = algorithm.Settle(Settlement(policy, 4, QuotaSettlementKind.Commit, account));
        var released = algorithm.Settle(Settlement(policy, 4, QuotaSettlementKind.Release, account));
        var expired = algorithm.Settle(Settlement(policy, 4, QuotaSettlementKind.Expire, account));

        Assert.Equal((0, 0, 2m), (committed.Committed, committed.Reserved, committed.Available));
        Assert.Equal((0, 0, 6m), (released.Committed, released.Reserved, released.Available));
        Assert.Equal((0, 0, 6m), (expired.Committed, expired.Reserved, expired.Available));
    }

    [Fact]
    public void Count_Concurrency_Leases_Without_Committing_Consumption() {
        var algorithm = new ConcurrencyQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.Concurrency, 2, TimeSpan.Zero);
        var account = Account(reserved: 1, available: 1);

        var exact = algorithm.Evaluate(Context(policy, 1, account));
        var overflow = algorithm.Evaluate(Context(policy, 1, Account(committed: 0, reserved: 2, available: 0)));
        var committed = algorithm.Settle(Settlement(policy, 1, QuotaSettlementKind.Commit, Account(reserved: 1, available: 1)));
        var released = algorithm.Settle(Settlement(policy, 1, QuotaSettlementKind.Release, Account(reserved: 1, available: 1)));

        Assert.True(exact.Allowed);
        Assert.Equal(2, exact.Reserved);
        Assert.False(overflow.Allowed);
        Assert.Equal(TimeSpan.Zero, overflow.RetryAfter);
        Assert.Equal((0, 0, 2m), (committed.Committed, committed.Reserved, committed.Available));
        Assert.Equal((0, 0, 2m), (released.Committed, released.Reserved, released.Available));
    }

    [Fact]
    public void Reject_Checked_Arithmetic_Overflow_Without_Wrapping() {
        var policy = Policy(QuotaConstants.Algorithms.FixedWindow, long.MaxValue, TimeSpan.FromMinutes(1));
        var algorithm = new FixedWindowQuotaAlgorithm();

        var decision = algorithm.Evaluate(Context(policy, 2, Account(committed: long.MaxValue - 1, available: 1)));

        Assert.False(decision.Allowed);
        Assert.Equal(long.MaxValue - 1, decision.Committed);
        Assert.Equal(0, decision.Reserved);
        Assert.Equal(1, decision.Available);
        Assert.Equal(TimeSpan.FromMinutes(1), decision.RetryAfter);
    }

    [Fact]
    public void Bound_Token_Retry_When_Charge_Exceeds_Representable_Duration() {
        var algorithm = new TokenBucketQuotaAlgorithm();
        var policy = Policy(QuotaConstants.Algorithms.TokenBucket, 1, TimeSpan.FromDays(365));

        var decision = algorithm.Evaluate(Context(policy, 1_000_000_000, Account(available: 1)));

        Assert.False(decision.Allowed);
        Assert.Equal(TimeSpan.MaxValue, decision.RetryAfter);
        Assert.Equal(1, decision.Available);
    }

    private static QuotaPolicy Policy(string algorithm, long limit, TimeSpan period, long burst = 1) {
        return new("requests", 1, algorithm, limit, period, burst);
    }

    private static SchemataQuotaAccount Account(
        long committed = 0,
        long reserved = 0,
        decimal available = 10,
        DateTimeOffset? windowStart = null
    ) {
        return new() {
            Uid = Guid.Parse("10000000-0000-0000-0000-000000000000"),
            Scope = "scope", Subject = "subject", Metric = "calls", PolicyName = "requests", PolicyVersion = 1,
            Committed = committed, Reserved = reserved, Available = available, WindowStart = windowStart ?? Now,
        };
    }

    private static SchemataQuotaSegment Segment(
        long committed,
        long reserved,
        DateTimeOffset start,
        DateTimeOffset expire
    ) {
        return new() {
            Uid = Guid.NewGuid(), AccountUid = Guid.Parse("10000000-0000-0000-0000-000000000000"),
            StartTime = start, ExpireTime = expire, Committed = committed, Reserved = reserved,
        };
    }

    private static QuotaAlgorithmContext Context(
        QuotaPolicy policy,
        long requested,
        SchemataQuotaAccount? account = null,
        IReadOnlyList<SchemataQuotaSegment>? segments = null,
        DateTimeOffset? now = null
    ) {
        return new(policy, new("calls"), requested, account ?? Account(), segments ?? [], now ?? Now, Guid.NewGuid());
    }

    private static QuotaSettlementContext Settlement(
        QuotaPolicy policy,
        long amount,
        QuotaSettlementKind kind,
        SchemataQuotaAccount account,
        IReadOnlyList<SchemataQuotaSegment>? segments = null
    ) {
        return new(policy, new("calls"), amount, kind, account, segments ?? [], Now, account.WindowStart,
            segments?.FirstOrDefault()?.Uid ?? Guid.NewGuid());
    }
}
