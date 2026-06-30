using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Identity.Skeleton.Entities;

/// <summary>
///     Identity role entity used by Schemata identity stores.
/// </summary>
[DisplayName("Role")]
[Table("SchemataRoles")]
[CanonicalName("roles/{role}")]
[PrimaryKey(nameof(Uid))]
public class SchemataRole : IdentityRole<Guid>, IIdentifier, ICanonicalName, IDescriptive, IConcurrency, ITimestamp
{
    [NotMapped]
    public override Guid Id
    {
        get => Uid;
        set => Uid = value;
    }

    [NotMapped]
    public override string? ConcurrencyStamp
    {
        get => Timestamp == Guid.Empty ? null : Timestamp.ToString();
        set => Timestamp = string.IsNullOrWhiteSpace(value) ? Identifiers.NewUid() : Guid.Parse(value);
    }

    #region ICanonicalName Members

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string?>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
