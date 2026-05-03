using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OAuth 2.0 Token Endpoint.
///     Dispatches the incoming <see cref="TokenRequest" /> to the <see cref="IGrantHandler" />
///     registered under the request's <c>grant_type</c> via keyed DI,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.3">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.3
///     </seealso>
///     .
/// </summary>
public sealed class TokenHandler(IServiceProvider sp) : TokenEndpoint
{
    /// <summary>
    ///     Resolves a keyed <see cref="IGrantHandler" /> by <see cref="TokenRequest.GrantType" />
    ///     and delegates token issuance to it.
    /// </summary>
    /// <param name="request">The parsed token request.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public override async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var grant = request.GrantType;

        var handler = sp.GetKeyedService<IGrantHandler>(grant);
        if (handler is null) {
            throw new OAuthException(OAuthErrors.UnsupportedGrantType, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.GrantType));
        }

        return await handler.HandleAsync(request, headers, ct);
    }
}
