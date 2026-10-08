using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the OAuth 2.0 Pushed Authorization Request endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-3">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §3: Pushed Authorization Request Endpoint
///     </seealso>
///     .
/// </summary>
public abstract class ParEndpoint
{
    /// <summary>Processes a pushed authorization request and returns a reference handle.</summary>
    public abstract Task<AuthorizationResult> ParAsync(
        AuthorizeRequest                  request,
        Dictionary<string, List<string?>> form,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}