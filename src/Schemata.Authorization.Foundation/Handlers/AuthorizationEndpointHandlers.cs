using Schemata.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Authorization.Foundation.Handlers;

internal sealed class AuthorizeEndpointHandler(AuthorizeEndpoint endpoint)
    : IRequestHandler<AuthorizeEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(AuthorizeEndpointRequest request, CancellationToken ct = default)
        => endpoint.AuthorizeAsync(request.Request, request.Principal!, ct);
}

internal sealed class TokenEndpointHandler(TokenEndpoint endpoint)
    : IRequestHandler<TokenEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(TokenEndpointRequest request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.Headers, ct);
}

internal sealed class RevokeEndpointHandler(RevocationEndpoint endpoint)
    : IRequestHandler<RevokeEndpointRequest, Unit>
{
    public async Task<Unit> HandleAsync(RevokeEndpointRequest request, CancellationToken ct = default)
    {
        await endpoint.HandleAsync(request.Request, request.Headers, ct);
        return Unit.Value;
    }
}

internal sealed class DeviceAuthorizeEndpointHandler(DeviceAuthorizeEndpoint endpoint)
    : IRequestHandler<DeviceAuthorizeEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(DeviceAuthorizeEndpointRequest request, CancellationToken ct = default)
        => endpoint.DeviceAuthorizeAsync(request.Request, request.Headers, ct);
}

internal sealed class EndSessionEndpointHandler(EndSessionEndpoint endpoint)
    : IRequestHandler<EndSessionEndpointRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(EndSessionEndpointRequest request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.Principal!, ct);
}

internal sealed class InteractionApproveHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionApproveRequest, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(InteractionApproveRequest request, CancellationToken ct = default)
        => endpoint.ApproveAsync(request.Request, request.Principal!, request.Issuer, ct);
}

internal sealed class InteractionDenyHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionDenyRequest, Unit>
{
    public async Task<Unit> HandleAsync(InteractionDenyRequest request, CancellationToken ct = default)
    {
        await endpoint.DenyAsync(request.Request, ct);
        return Unit.Value;
    }
}

internal sealed class IntrospectionEndpointHandler(IntrospectionEndpoint endpoint)
    : IRequestHandler<IntrospectionEndpointQuery, IntrospectionResponse>
{
    public Task<IntrospectionResponse> HandleAsync(IntrospectionEndpointQuery request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Request, request.Headers, ct);
}

internal sealed class UserInfoEndpointHandler(UserInfoEndpoint endpoint)
    : IRequestHandler<UserInfoEndpointQuery, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(UserInfoEndpointQuery request, CancellationToken ct = default)
        => endpoint.HandleAsync(request.Principal!, ct);
}

internal sealed class InteractionDetailsHandler(InteractionEndpoint endpoint)
    : IRequestHandler<InteractionDetailsQuery, AuthorizationResult>
{
    public Task<AuthorizationResult> HandleAsync(InteractionDetailsQuery request, CancellationToken ct = default)
        => endpoint.GetDetailsAsync(request.Request, request.Issuer, ct);
}
