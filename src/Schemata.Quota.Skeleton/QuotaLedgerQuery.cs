namespace Schemata.Quota.Skeleton;

public sealed record QuotaLedgerQuery(QuotaScope Scope, QuotaSubject Subject, QuotaMetric? Metric, int PageSize);
