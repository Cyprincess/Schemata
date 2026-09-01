using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class TokenEndpointHandler(TokenEndpoint endpoint)
    : IRequestHandler<TokenEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(TokenEndpointRequest request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.Headers, ct);
}