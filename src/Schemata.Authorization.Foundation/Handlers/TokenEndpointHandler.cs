using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class TokenEndpointHandler(TokenEndpoint endpoint)
    : IRequestHandler<TokenEndpointRequest, AuthorizationResult>
{
    public async Task<AuthorizationResult> HandleAsync(TokenEndpointRequest request, CancellationToken ct = default) {
        var ctx = AdviceContext.Require();

        // Grant handlers consume the header map for client authentication and then drop it,
        // while the DPoP advisor still needs the proof from the ambient context.
        ctx.Set(new DpopProof(request.Headers is not null
            && request.Headers.TryGetValue(Headers.Dpop, out var values)
            ? values.Find(v => !string.IsNullOrWhiteSpace(v))
            : null));

        var result = await endpoint.HandleAsync(request.Request, request.Headers, ct);

        // The dispatcher's ambient context does not outlive this dispatch; carry the DPoP
        // key binding to the sign-in issuer through the result properties.
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