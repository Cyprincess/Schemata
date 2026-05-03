using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the RP-Initiated Logout endpoint,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     ,
///     OpenID Connect RP-Initiated Logout 1.0.
/// </summary>
public abstract class EndSessionEndpoint
{
    /// <summary>Processes an end-session request from an authenticated user.</summary>
    public abstract Task<AuthorizationResult> HandleAsync(
        EndSessionRequest request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    );
}
