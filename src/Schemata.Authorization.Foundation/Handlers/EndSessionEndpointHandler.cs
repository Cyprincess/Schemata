using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class EndSessionEndpointHandler(EndSessionEndpoint endpoint)
    : IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(EndSessionEndpointRequest request, CancellationToken ct = default) {
        if (request.Principal is null) {
            return Task.FromResult(AuthorizationResult.Challenge());
        }

        return endpoint.HandleAsync(request.Request, request.Principal, ct);
    }
}