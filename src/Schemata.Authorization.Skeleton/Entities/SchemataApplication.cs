using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 / OpenID Connect client application registered with the authorization server.
/// </summary>
/// <remarks>
///     Maps to the OpenIddict application entity. Each record defines a client with its credentials,
///     redirect URIs, permissions, and consent requirements.
/// </remarks>
[DisplayName("Application")]
[Table("SchemataApplications")]
[CanonicalName("applications/{application}")]
public class SchemataApplication : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    /// <summary>Gets or sets the application type (e.g. "web", "native").</summary>
    public virtual string? ApplicationType { get; set; }

    /// <summary>Gets or sets the client identifier issued to the application during registration.</summary>
    public virtual string? ClientId { get; set; }

    /// <summary>Gets or sets the hashed client secret.</summary>
    public virtual string? ClientSecret { get; set; }

    /// <summary>Gets or sets the client type (e.g. "confidential", "public").</summary>
    public virtual string? ClientType { get; set; }

    /// <summary>Gets or sets the consent type (e.g. "explicit", "implicit", "external", "systematic").</summary>
    public virtual string? ConsentType { get; set; }

    /// <summary>Gets or sets the JSON-serialized custom properties associated with the application.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the JSON Web Key Set used for token validation.</summary>
    public virtual string? JsonWebKeySet { get; set; }

    /// <summary>Gets or sets the JSON-serialized post-logout redirect URIs allowed for the application.</summary>
    public virtual string? PostLogoutRedirectUris { get; set; }

    /// <summary>Gets or sets the JSON-serialized redirect URIs allowed for the application.</summary>
    public virtual string? RedirectUris { get; set; }

    /// <summary>Gets or sets the JSON-serialized permissions granted to the application.</summary>
    public virtual string? Permissions { get; set; }

    /// <summary>Gets or sets the JSON-serialized requirements enforced for the application.</summary>
    public virtual string? Requirements { get; set; }

    /// <summary>Gets or sets the JSON-serialized settings for the application.</summary>
    public virtual string? Settings { get; set; }

    #region ICanonicalName Members

    /// <summary>Gets or sets the unique name, backed by <see cref="ClientId" />.</summary>
    public virtual string? Name
    {
        get => ClientId;
        set => ClientId = value;
    }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDisplayName Members

    /// <inheritdoc />
    public virtual string? DisplayName { get; set; }

    /// <summary>Gets or sets the JSON-serialized localized display names.</summary>
    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
