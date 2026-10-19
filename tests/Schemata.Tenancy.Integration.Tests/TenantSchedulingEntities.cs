using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ProtoBuf;
using Schemata.Abstractions.Entities;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Tenancy.Integration.Tests;

[Table("TenantJobs")]
[CanonicalName("jobs/{job}")]
[Index(nameof(Key), IsUnique = true, Filter = "TenantUid IS NULL")]
[Index(nameof(TenantUid), nameof(Key), IsUnique = true, Filter = "TenantUid IS NOT NULL")]
public sealed class TenantJob : SchemataJob, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantJobExecutions")]
[CanonicalName("operations/{operation}")]
public sealed class TenantJobExecution : SchemataJobExecution, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}
