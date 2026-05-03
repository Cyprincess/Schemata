using System.Security.Claims;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the refresh token pipeline.
///     Consumed by <see cref="Advisors.IRefreshTokenAdvisor{TApplication, TToken}" />.
/// </summary>
public sealed class RefreshTokenContext<TApplication, TToken>
    where TApplication : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>Token endpoint request containing the refresh token.</summary>
    public TokenRequest? Request { get; set; }

    /// <summary>Resolved client application.</summary>
    public TApplication? Application { get; set; }

    /// <summary>The refresh token entity found by resolving the <c>refresh_token</c> from the request.</summary>
    public TToken? Token { get; set; }

    /// <summary>Claims principal derived from the resolved refresh token.</summary>
    public ClaimsPrincipal? Principal { get; set; }
}
