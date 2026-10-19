using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using ProtoBuf;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

[Table("TenantFlowProcesses")]
[CanonicalName("processes/{process}")]
[Index(nameof(DefinitionName), nameof(IdempotencyKey), IsUnique = true, Filter = "TenantUid IS NULL")]
[Index(nameof(TenantUid), nameof(DefinitionName), nameof(IdempotencyKey), IsUnique = true, Filter = "TenantUid IS NOT NULL")]
public sealed class TenantProcess : SchemataProcess, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowProcessTokens")]
[CanonicalName("processes/{process}/tokens/{token}")]
public sealed class TenantProcessToken : SchemataProcessToken, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowProcessTransitions")]
[CanonicalName("processes/{process}/transitions/{transition}")]
public sealed class TenantProcessTransition : SchemataProcessTransition, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowProcessCompensations")]
[CanonicalName("process-compensations/{process_compensation}")]
public sealed class TenantProcessCompensation : SchemataProcessCompensation, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowProcessParticipants")]
[Index(nameof(TenantUid), nameof(Subject), nameof(Process))]
public sealed class TenantProcessParticipant : SchemataProcessParticipant, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowProcessSources")]
[Index(nameof(Process), nameof(Token), nameof(Name), IsUnique = true, Filter = "TenantUid IS NULL AND Token IS NOT NULL")]
[Index(nameof(Process), nameof(Name), IsUnique = true, Filter = "TenantUid IS NULL AND Token IS NULL")]
[Index(nameof(TenantUid), nameof(Process), nameof(Token), nameof(Name), IsUnique = true, Filter = "TenantUid IS NOT NULL AND Token IS NOT NULL")]
[Index(nameof(TenantUid), nameof(Process), nameof(Name), IsUnique = true, Filter = "TenantUid IS NOT NULL AND Token IS NULL")]
public sealed class TenantProcessSource : SchemataProcessSource, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}

[Table("TenantFlowEffectIntents")]
[Index(nameof(RequestId), IsUnique = true, Filter = "TenantUid IS NULL")]
[Index(nameof(TenantUid), nameof(RequestId), IsUnique = true, Filter = "TenantUid IS NOT NULL")]
public sealed class TenantFlowEffectIntent : SchemataFlowEffectIntent, ITenant
{
    [JsonIgnore, ProtoIgnore]
    public Guid? TenantUid { get; set; }
}
