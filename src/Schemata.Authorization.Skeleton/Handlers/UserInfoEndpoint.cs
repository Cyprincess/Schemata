using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the UserInfo endpoint,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     .
/// </summary>
public abstract class UserInfoEndpoint
{
    /// <summary>Processes a UserInfo request and returns claims for the authenticated principal.</summary>
    public abstract Task<AuthorizationResult> HandleAsync(ClaimsPrincipal principal, CancellationToken ct);
}
