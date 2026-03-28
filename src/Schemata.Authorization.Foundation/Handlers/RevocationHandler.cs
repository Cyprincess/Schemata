using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class RevocationHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    ITokenManager<TToken>              tokens,
    IServiceProvider                   sp
) : RevocationEndpoint
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    public override async Task HandleAsync(
        RevokeRequest                      request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Token)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.Token));
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            return;
        }

        var entity = await tokens.FindByReferenceIdAsync(request.Token, ct);
        if (entity is null) {
            return;
        }

        var ctx = new AdviceContext(sp);

        switch (await Advisor.For<IRevocationAdvisor<TApp, TToken>>()
                             .RunAsync(ctx, application, request, entity, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return;
            case AdviseResult.Block:
            default:
                return;
        }

        await tokens.RevokeAsync(entity, ct);
    }
}
