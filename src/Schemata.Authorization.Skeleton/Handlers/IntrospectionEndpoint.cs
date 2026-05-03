using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the OAuth 2.0 token introspection endpoint,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html">RFC 7662: OAuth 2.0 Token Introspection</seealso>
///     .
/// </summary>
public abstract class IntrospectionEndpoint
{
    /// <summary>Processes an introspection request and returns metadata about the token.</summary>
    public abstract Task<IntrospectionResponse> HandleAsync(
        IntrospectRequest                  request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
