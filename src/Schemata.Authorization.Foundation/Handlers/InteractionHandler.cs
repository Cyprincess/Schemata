using System;
using System.Security.Claims;
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

public sealed class InteractionHandler(IServiceProvider sp) : InteractionEndpoint
{
    public override Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    ) {
        var handler = sp.GetKeyedService<IInteractionHandler>(request.CodeType);
        if (handler == null) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), request.CodeType));
        }

        return handler.GetDetailsAsync(request, issuer, ct);
    }

    public override Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    ) {
        var handler = sp.GetKeyedService<IInteractionHandler>(request.CodeType);
        if (handler == null) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), request.CodeType));
        }

        return handler.ApproveAsync(request, principal, issuer, ct);
    }

    public override async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var handler = sp.GetKeyedService<IInteractionHandler>(request.CodeType);
        if (handler == null) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), request.CodeType));
        }

        await handler.DenyAsync(request, ct);
    }
}
