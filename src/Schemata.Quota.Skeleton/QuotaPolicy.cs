using System;

namespace Schemata.Quota.Skeleton;

public sealed record QuotaPolicy(string Name, int Version, string Algorithm, long Limit, TimeSpan Period, long Burst);
