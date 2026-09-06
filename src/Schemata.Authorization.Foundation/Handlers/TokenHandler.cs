using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
/// <remarks>
///     Grant handlers consume the header map for client authentication and then drop it, so the
///     DPoP proof is published to the ambient context for the proof advisor, and a granted
///     binding crosses the dispatch boundary back to the sign-in issuer through the result
///     properties.
/// </remarks>
public sealed class TokenHandler(IServiceProvider sp) : TokenEndpoint
{
    public override async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var ctx = AdviceContext.Require();

        ctx.Set(new DpopProof(headers is not null
            && headers.TryGetValue(Headers.Dpop, out var values)
            ? values.Find(v => !string.IsNullOrWhiteSpace(v))
            : null));

        var grant = request.GrantType;

        var handler = sp.GetKeyedService<IGrantHandler>(grant);
        if (handler is null) {
            throw new OAuthException(
                OAuthErrors.UnsupportedGrantType,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.GrantType)
            );
        }

        var result = await handler.HandleAsync(request, headers, ct);

        if (result.Status == AuthorizationStatus.SignIn
         && ctx.TryGet<DpopBinding>(out var binding)
         && binding is not null) {
            if (result.Properties is null) {
                throw new InvalidOperationException(
                    "The sign-in result carries no properties to attach the DPoP binding to.");
            }

            result.Properties[Properties.DpopJkt] = binding.Jkt;
        }

        return result;
    }
}
