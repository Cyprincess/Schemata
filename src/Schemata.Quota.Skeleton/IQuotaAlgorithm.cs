using System;
using System.Collections.Generic;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Skeleton;

public interface IQuotaAlgorithm
{
    QuotaDecision Evaluate(QuotaAlgorithmContext context);
    QuotaAccountTransition Settle(QuotaSettlementContext context);
}

public sealed record QuotaAlgorithmContext(
    QuotaPolicy Policy,
    QuotaMetric Metric,
    long Requested,
    SchemataQuotaAccount Account,
    IReadOnlyList<SchemataQuotaSegment> Segments,
    DateTimeOffset Now,
    Guid AllocationUid);

public sealed record QuotaDecision(
    bool Allowed,
    long Committed,
    long Reserved,
    decimal Available,
    DateTimeOffset WindowStart,
    TimeSpan? RetryAfter,
    IReadOnlyList<SchemataQuotaSegment> Segments);

public enum QuotaSettlementKind
{
    Commit,
    Release,
    Expire,
}

public sealed record QuotaSettlementContext(
    QuotaPolicy Policy,
    QuotaMetric Metric,
    long Amount,
    QuotaSettlementKind Kind,
    SchemataQuotaAccount Account,
    IReadOnlyList<SchemataQuotaSegment> Segments,
    DateTimeOffset Now,
    DateTimeOffset WindowStart,
    Guid AllocationUid);

public sealed record QuotaAccountTransition(
    long Committed,
    long Reserved,
    decimal Available,
    DateTimeOffset WindowStart,
    IReadOnlyList<SchemataQuotaSegment> Segments);
