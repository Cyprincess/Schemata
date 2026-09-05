using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class InteractionApproveHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionApproveRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(InteractionApproveRequest request, CancellationToken ct = default) {
        if (request.Principal is null) {
            return Task.FromResult(AuthorizationResult.Challenge());
        }

        return endpoint.ApproveAsync(request.Request, request.Principal, request.Issuer, ct);
    }
}