using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

[Table("Applications")]
public class SchemataApplication : IIdentifier, IConcurrency, ITimestamp
{
    public virtual string? ApplicationType { get; set; }

    public virtual string? ClientId { get; set; }

    public virtual string? ClientSecret { get; set; }

    public virtual string? ClientType { get; set; }

    public virtual string? ConsentType { get; set; }

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Properties { get; set; }

    public virtual string? JsonWebKeySet { get; set; }

    public virtual List<string>? PostLogoutRedirectUris { get; set; }

    public virtual List<string>? RedirectUris { get; set; }

    public virtual List<string>? Permissions { get; set; }

    public virtual List<string>? Requirements { get; set; }

    public virtual Dictionary<string, string>? Settings { get; set; }

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate     { get; set; }
    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}