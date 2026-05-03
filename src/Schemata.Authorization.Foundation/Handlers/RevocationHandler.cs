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

/// <summary>
///     Token Revocation endpoint.
///     Looks up the token by reference ID and revokes it.
///     Revocation is idempotent — missing tokens or invalid client credentials
///     result in a successful response without an error to avoid leaking token existence,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html#section-2.2">
///         RFC 7009: OAuth 2.0 Token Revocation
///         §2.2: Revocation Response
///     </seealso>
///     .
/// </summary>
public sealed class RevocationHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    ITokenManager<TToken>              tokens,
    IServiceProvider                   sp
) : RevocationEndpoint
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>
    ///     Revokes a token per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc7009.html#section-2.2">
    ///         RFC 7009: OAuth 2.0 Token Revocation
    ///         §2.2: Revocation Response
    ///     </seealso>
    ///     .
    ///     Returns silently when the client is not authenticated
    ///     or the token is not found. Runs the
    ///     <see cref="IRevocationAdvisor{TApp,TToken}" /> pipeline before committing
    ///     the revocation.
    /// </summary>
    /// <param name="request">Revocation request containing the token to revoke.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
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
            // do not reveal token validity to unauthenticated callers.
            // See RFC 7009 §2.2.
            return;
        }

        var entity = await tokens.FindByReferenceIdAsync(request.Token, ct);
        if (entity is null) {
            // do not reveal token validity for unknown tokens.
            // See RFC 7009 §2.2.
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
