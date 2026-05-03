using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Token Introspection endpoint.
///     Authenticates the caller client, validates the token's signature,
///     and returns an <see cref="IntrospectionResponse" /> with token metadata.
///     Inactive or invalid tokens return <c>{ active: false }</c>,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.2">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.2: Introspection Response
///     </seealso>
///     .
/// </summary>
public sealed class IntrospectionHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    TokenService                       issuer,
    ITokenManager<TToken>              tokens,
    IServiceProvider                   sp
) : IntrospectionEndpoint
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    /// <summary>
    ///     Introspects the given token: looks up the stored entity, validates
    ///     the JWT payload, runs the <see cref="IIntrospectionAdvisor{TApp,TToken}" />
    ///     pipeline, and populates the response.
    /// </summary>
    /// <param name="request">Introspection request containing the token.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public override async Task<IntrospectionResponse> HandleAsync(
        IntrospectRequest                  request,
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
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        var entity = await tokens.FindByReferenceIdAsync(request.Token, ct);
        if (string.IsNullOrWhiteSpace(entity?.Payload)) {
            return new() { Active = false };
        }

        var principal = await issuer.Validate(entity.Payload);
        if (principal is null) {
            return new() { Active = false };
        }

        var ctx = new AdviceContext(sp);

        var introspection = new IntrospectionContext<TApp, TToken> {
            Application = application,
            Request     = request,
            Token       = entity,
            Principal   = principal,
            Response = new() {
                Active    = true,
                Scope     = principal.FindFirstValue(Claims.Scope),
                ClientId  = principal.FindFirstValue(Claims.ClientId),
                Username  = principal.FindFirstValue(Claims.Name),
                TokenType = Schemes.Bearer,
                Exp       = GetUnixTimestamp(principal, Claims.Expiration),
                Iat       = GetUnixTimestamp(principal, Claims.IssuedAt),
                Nbf       = GetUnixTimestamp(principal, Claims.NotBefore),
                Sub       = principal.FindFirstValue(Claims.Subject),
                Aud       = principal.FindFirstValue(Claims.Audience),
                Iss       = principal.FindFirstValue(Claims.Issuer),
                Jti       = principal.FindFirstValue(Claims.JwtId),
            },
        };

        switch (await Advisor.For<IIntrospectionAdvisor<TApp, TToken>>()
                             .RunAsync(ctx, introspection, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                break;
            case AdviseResult.Block:
            default:
                return new() { Active = false };
        }

        return introspection.Response;
    }

    private static long? GetUnixTimestamp(ClaimsPrincipal principal, string type) {
        var value = principal.FindFirstValue(type);
        return !string.IsNullOrWhiteSpace(value) && long.TryParse(value, out var result) ? result : null;
    }
}
