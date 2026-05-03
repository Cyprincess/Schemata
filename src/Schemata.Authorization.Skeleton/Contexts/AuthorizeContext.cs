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

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>Authenticated resource owner principal. Non-null after successful authentication.</summary>
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>Negotiated response mode, e.g. <c>"query"</c>, <c>"fragment"</c>, or <c>"form_post"</c>.</summary>
    public string? ResponseMode { get; set; }

    /// <summary>Current consent decision for this authorization request.</summary>
    public ConsentDecision ConsentDecision { get; set; }

    /// <summary>Whether the user must re-authenticate, e.g. due to an expired <c>max_age</c>.</summary>
    public bool RequireReauthentication { get; set; }
}
