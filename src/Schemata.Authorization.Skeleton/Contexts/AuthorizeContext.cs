using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the authorization endpoint pipeline.
///     Populated by the authorize handler and consumed by <see cref="Advisors.IAuthorizeAdvisor{TApplication}" />.
/// </summary>
public sealed class AuthorizeContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Parsed authorization request parameters.</summary>
    public AuthorizeRequest? Request { get; set; }

    public AuthorizationRequestStage Stage { get; set; }
    public string? DpopProof { get; set; }
    public ClaimsRequest? RequestedClaims { get; set; }
    public string? SessionId { get; set; }
    public string? SessionSubject { get; set; }
    public string? SessionStateSalt { get; set; }
    public AuthorizationResult? Result { get; set; }
    public string? ResolvedRequestUri { get; set; }
    public string? ResolvedRequestObject { get; set; }
    public string? AuthorizationDetails { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>Authenticated resource owner principal after successful authentication.</summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    ///     Authentication evidence resolved once for this authorization event. Request-policy
    ///     advisors and issuance consume the same snapshot; a continuation restores it instead of
    ///     calling the provider again.
    /// </summary>
    public Services.AuthenticationContext? Authentication { get; set; }

    /// <summary>Negotiated response mode, e.g. <c>"query"</c>, <c>"fragment"</c>, or <c>"form_post"</c>.</summary>
    public string? ResponseMode { get; set; }

    /// <summary>
    ///     The single validated callback context, captured once the client and redirect URI have
    ///     been validated after PAR/JAR normalization: the trusted redirect, the caller state, and
    ///     the legal effective response mode. Advisors and handlers finalize every failure from
    ///     this fact — raw inputs are never re-read and an invalid redirect never becomes a
    ///     callback.
    /// </summary>
    public AuthorizationCallback? Callback { get; set; }

    /// <summary>Current consent decision for this authorization request.</summary>
    public ConsentDecision ConsentDecision { get; set; }

    /// <summary>Whether the user must re-authenticate, e.g. due to an expired <c>max_age</c>.</summary>
    public bool RequireReauthentication { get; set; }

/// <summary>The validated authorization callback facts shared by success and failure finalization.</summary>
/// <param name="RedirectUri">The redirect URI validated against the client registration.</param>
/// <param name="State">The caller state, preserved through every finalized response.</param>
/// <param name="ResponseMode">The legal effective response mode for this request.</param>
public sealed record AuthorizationCallback(string RedirectUri, string? State, string ResponseMode);
}
