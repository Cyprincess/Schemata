namespace Schemata.Quota.Skeleton;

public sealed record QuotaCharge(QuotaPolicy Policy, QuotaMetric Metric, long Amount);
