using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the OAuth 2.0 token endpoint.
///     Delegates to registered <see cref="IGrantHandler" /> implementations based on the
///     <c>grant_type</c> parameter,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.2">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §3.2: Token Endpoint
///     </seealso>
///     .
/// </summary>
public abstract class TokenEndpoint
{
    /// <summary>Processes a token request.</summary>
    public abstract Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
