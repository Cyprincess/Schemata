using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class UserInfoEndpointHandler(UserInfoEndpoint endpoint)
    : IRequestHandler<UserInfoEndpointQuery, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(UserInfoEndpointQuery request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Principal!, ct);
}