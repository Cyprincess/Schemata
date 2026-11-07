namespace Schemata.Quota.Skeleton;

public sealed record QuotaAccountView(
    QuotaPolicy Policy,
    QuotaMetric Metric,
    long Committed,
    long Reserved,
    decimal Available);
