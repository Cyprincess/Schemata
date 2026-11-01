using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>Detailed response body for <see cref="Entities.SchemataApplication" /> resources; mirrors
/// <see cref="ApplicationRequest" /> so management reads back every fact it can write.</summary>
public class ApplicationDetail : IIdentifier, ICanonicalName, IDescriptive, ITimestamp, IFreshness
{
    /// <summary>OAuth 2.0 client identifier.</summary>
    public string? ClientId { get; set; }

    /// <summary>Application type, such as <c>"web"</c> or <c>"native"</c>.</summary>
    public string? ApplicationType { get; set; }

    /// <summary><c>token_endpoint_auth_method</c>.</summary>
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary><c>token_endpoint_auth_signing_alg</c>.</summary>
    public string? TokenEndpointAuthSigningAlg { get; set; }

    /// <summary><c>grant_types</c> the client may use.</summary>
    public ICollection<string>? GrantTypes { get; set; }

    /// <summary><c>response_types</c> the client may request.</summary>
    public ICollection<string>? ResponseTypes { get; set; }

    /// <summary><c>scope</c> the client may request, space-delimited.</summary>
    public string? Scope { get; set; }

    /// <summary><c>id_token_signed_response_alg</c>.</summary>
    public string? IdTokenSignedResponseAlg { get; set; }

    /// <summary><c>userinfo_signed_response_alg</c>.</summary>
    public string? UserinfoSignedResponseAlg { get; set; }

    /// <summary><c>userinfo_encrypted_response_alg</c>.</summary>
    public string? UserinfoEncryptedResponseAlg { get; set; }

    /// <summary><c>userinfo_encrypted_response_enc</c>.</summary>
    public string? UserinfoEncryptedResponseEnc { get; set; }

    /// <summary>Registered redirect URIs.</summary>
    public ICollection<string>? RedirectUris { get; set; }

    /// <summary>Administrator-granted permission entries, such as endpoint permissions.</summary>
    public ICollection<string>? Permissions { get; set; }

    /// <summary>Allowed post-logout redirect URIs for RP-Initiated Logout.</summary>
    public ICollection<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>Application-specific subject identifier type override.</summary>
    public string? SubjectType { get; set; }

    /// <summary>Sector identifier URI for pairwise subject identifiers.</summary>
    public string? SectorIdentifierUri { get; set; }

    /// <summary><c>default_max_age</c> in seconds, carried as text to match the entity.</summary>
    public string? DefaultMaxAge { get; set; }

    /// <summary><c>default_acr_values</c>.</summary>
    public ICollection<string>? DefaultAcrValues { get; set; }

    /// <summary><c>require_auth_time</c>.</summary>
    public bool RequireAuthTime { get; set; }

    /// <summary><c>initiate_login_uri</c>.</summary>
    public string? InitiateLoginUri { get; set; }

    /// <summary>Front-channel logout URI.</summary>
    public string? FrontChannelLogoutUri { get; set; }

    /// <summary>Whether front-channel logout requests include the OP session identifier.</summary>
    public bool FrontChannelLogoutSessionRequired { get; set; }

    /// <summary>Back-channel logout URI.</summary>
    public string? BackChannelLogoutUri { get; set; }

    /// <summary>Whether back-channel logout tokens include the OP session identifier.</summary>
    public bool BackChannelLogoutSessionRequired { get; set; }

    /// <summary><c>require_pushed_authorization_requests</c>; binds the client to PAR for every authorization request (RFC 9126 §6).</summary>
    public bool? RequirePushedAuthorizationRequests { get; set; }

    /// <summary><c>request_object_signing_alg</c>; the JWS algorithm registered for request objects (RFC 9101 §4).</summary>
    public string? RequestObjectSigningAlg { get; set; }

    /// <summary><c>require_signed_request_object</c>; requires the server to reject unsigned request objects for this client (RFC 9101 §10.5).</summary>
    public bool? RequireSignedRequestObject { get; set; }

    /// <summary><c>authorization_details_types</c> the client may use in <c>authorization_details</c> objects (RFC 9396 §10).</summary>
    public ICollection<string>? AuthorizationDetailsTypes { get; set; }

    /// <summary><c>dpop_bound_access_tokens</c>; token requests without a DPoP proof are rejected (RFC 9449 §5.2).</summary>
    public bool DpopBoundAccessTokens { get; set; }

    /// <summary><c>software_id</c>.</summary>
    public string? SoftwareId { get; set; }

    /// <summary><c>software_version</c>.</summary>
    public string? SoftwareVersion { get; set; }

    /// <summary><c>software_statement</c>, stored verbatim.</summary>
    public string? SoftwareStatement { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public Dictionary<string, string>? LocalizedMetadata { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
