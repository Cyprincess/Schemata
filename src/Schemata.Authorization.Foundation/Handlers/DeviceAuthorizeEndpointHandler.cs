using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class DeviceAuthorizeEndpointHandler(DeviceAuthorizeEndpoint endpoint)
    : IRequestHandler<DeviceAuthorizeEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(DeviceAuthorizeEndpointRequest request, CancellationToken ct = default)
        => endpoint.DeviceAuthorizeAsync(request.Request, request.Headers, ct);
}