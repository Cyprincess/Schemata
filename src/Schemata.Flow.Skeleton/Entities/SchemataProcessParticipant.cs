using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Entities;

[Table("SchemataProcessParticipants")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Subject), nameof(Process))]
public class SchemataProcessParticipant : IIdentifier, IConcurrency, ITimestamp
{
    public virtual Guid Uid { get; set; }
    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }
    [ResourceReference(typeof(SchemataProcess))]
    public virtual string Process { get; set; } = null!; // EF Core materializes this required column.
    public virtual string Subject { get; set; } = null!; // EF Core materializes this required column.
    public virtual ProcessParticipationKind Kind { get; set; }
    [ResourceReference(typeof(SchemataProcessToken))]
    public virtual string? Token { get; set; }
    public virtual string? Activity { get; set; }
    public virtual DateTime? ExpiresAt { get; set; }
    public virtual DateTime? RevokedAt { get; set; }
    public virtual DateTime? CreateTime { get; set; }
    public virtual DateTime? UpdateTime { get; set; }
}

public enum ProcessParticipationKind
{
    Participation,
    Eligibility,
}
