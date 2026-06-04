using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Create/update request body for <see cref="Entities.SchemataApplication" />.</summary>
public class ApplicationRequest : ICanonicalName, IDescriptive, IFreshness
{
    public string? ClientId { get; set; }

    public string? ClientType { get; set; }

    public string? ApplicationType { get; set; }

    public string? ConsentType { get; set; }

    public bool? RequirePkce { get; set; }

    public ICollection<string>? RedirectUris { get; set; }

    public ICollection<string>? Permissions { get; set; }

    public ICollection<string>? PostLogoutRedirectUris { get; set; }

    public string? SubjectType { get; set; }

    public string? SectorIdentifierUri { get; set; }

    public string? FrontChannelLogoutUri { get; set; }

    public bool FrontChannelLogoutSessionRequired { get; set; }

    public string? BackChannelLogoutUri { get; set; }

    public bool BackChannelLogoutSessionRequired { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                     DisplayName  { get; set; }
    public Dictionary<string, string>? DisplayNames { get; set; }
    public string?                     Description  { get; set; }
    public Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
