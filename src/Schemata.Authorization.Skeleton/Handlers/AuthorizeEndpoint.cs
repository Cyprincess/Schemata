using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the OAuth 2.0 authorization endpoint.
///     Implementations run the full authorization pipeline and return a
///     <see cref="AuthorizationResult" />,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §3.1: Authorization Endpoint
///     </seealso>
///     .
/// </summary>
public abstract class AuthorizeEndpoint
{
    /// <summary>Processes an authorization request from an authenticated user.</summary>
    public abstract Task<AuthorizationResult> AuthorizeAsync(
        AuthorizeRequest  request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    );
}
