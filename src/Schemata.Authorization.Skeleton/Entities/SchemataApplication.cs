using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Application")]
[Table("SchemataApplications")]
[CanonicalName("applications/{application}")]
public class SchemataApplication : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    public virtual string? ApplicationType { get; set; }

    public virtual string? ClientId { get; set; }

    public virtual string? ClientSecret { get; set; }

    public virtual string? ClientType { get; set; }

    public virtual string? ConsentType { get; set; }

    public virtual string? Properties { get; set; }

    public virtual string? JsonWebKeySet { get; set; }

    public virtual string? PostLogoutRedirectUris { get; set; }

    public virtual string? RedirectUris { get; set; }

    public virtual string? Permissions { get; set; }

    public virtual string? Requirements { get; set; }

    public virtual string? Settings { get; set; }

    #region ICanonicalName Members

    public virtual string? Name
    {
        get => ClientId;
        set => ClientId = value;
    }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDisplayName Members

    public virtual string? DisplayName { get; set; }

    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime     { get; set; }
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
