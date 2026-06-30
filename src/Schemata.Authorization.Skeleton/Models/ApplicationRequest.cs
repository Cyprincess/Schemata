using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Create or update request body for <see cref="Entities.SchemataApplication" /> resources.</summary>
public class ApplicationRequest : ICanonicalName, IDescriptive, IFreshness
{
    /// <summary>OAuth 2.0 client identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth 2.0 client type, such as <c>"confidential"</c> or <c>"public"</c>.</summary>
    public string? ClientType { get; set; }

    /// <summary>Application type, such as <c>"web"</c> or <c>"native"</c>.</summary>
    public string? ApplicationType { get; set; }

    /// <summary>Consent model for authorization requests.</summary>
    public string? ConsentType { get; set; }

    /// <summary>Application-specific PKCE requirement override.</summary>
    public bool? RequirePkce { get; set; }

    /// <summary>Registered redirect URIs.</summary>
    public ICollection<string>? RedirectUris { get; set; }

    /// <summary>Granted permissions, such as endpoint, grant type, and scope permissions.</summary>
    public ICollection<string>? Permissions { get; set; }

    /// <summary>Allowed post-logout redirect URIs for RP-Initiated Logout.</summary>
    public ICollection<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>Application-specific subject identifier type override.</summary>
    public string? SubjectType { get; set; }

    /// <summary>Sector identifier URI for pairwise subject identifiers.</summary>
    public string? SectorIdentifierUri { get; set; }

    /// <summary>Front-channel logout URI.</summary>
    public string? FrontChannelLogoutUri { get; set; }

    /// <summary>Whether front-channel logout requests include the OP session identifier.</summary>
    public bool FrontChannelLogoutSessionRequired { get; set; }

    /// <summary>Back-channel logout URI.</summary>
    public string? BackChannelLogoutUri { get; set; }

    /// <summary>Whether back-channel logout tokens include the OP session identifier.</summary>
    public bool BackChannelLogoutSessionRequired { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion
}
