using System;
using System.Collections.Generic;
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

public sealed class TokenHandler(IServiceProvider sp) : TokenEndpoint
{
    public override async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var grant = request.GrantType;

        var handler = sp.GetKeyedService<IGrantHandler>(grant);
        if (handler is null) {
            throw new OAuthException(OAuthErrors.UnsupportedGrantType, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1015), Parameters.GrantType));
        }

        return await handler.HandleAsync(request, headers, ct);
    }
}
