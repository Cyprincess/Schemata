using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the refresh token pipeline.
///     Consumed by <see cref="Advisors.IRefreshTokenAdvisor{TApplication}" />.
/// </summary>
public sealed class RefreshTokenContext<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>Token endpoint request containing the refresh token.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The refresh token entity found by resolving the <c>refresh_token</c> from the request.</summary>
    public SchemataToken? Token { get; set; }

    /// <summary>Claims principal derived from the resolved refresh token.</summary>
    public ClaimsPrincipal? Principal { get; set; }
    public string? AuthorizationDetails { get; set; }

    /// <summary>
    ///     The narrowed authorization grant established by the handler before the pipeline runs;
    ///     device-secret advisors read its profile, scope, and family state from here.
    /// </summary>
    public AuthorizationGrantContext? Grant { get; set; }

    /// <summary>The DPoP proof key binding validated at the request stage.</summary>
    public DpopBinding? Dpop { get; set; }

    /// <summary>The device secret issued or reused by the device SSO advisor.</summary>
    public DeviceSecretIssuance? DeviceSecret { get; set; }

    /// <summary>
    ///     The response written by an advisor returning
    ///     <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle" />.
    /// </summary>
    public AuthorizationResult? Result { get; set; }
}
