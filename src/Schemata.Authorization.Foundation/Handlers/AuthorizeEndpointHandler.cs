using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class AuthorizeEndpointHandler(AuthorizeEndpoint endpoint)
    : IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(AuthorizeEndpointRequest request, CancellationToken ct = default) {
        if (request.Principal is null) {
            return Task.FromResult(AuthorizationResult.Challenge());
        }

        return endpoint.AuthorizeAsync(request.Request, request.Principal, ct);
    }
}