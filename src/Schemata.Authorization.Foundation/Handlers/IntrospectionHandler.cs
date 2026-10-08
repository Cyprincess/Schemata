using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
public sealed class IntrospectionHandler<TApp>(
    IClientAuthenticationService<TApp>     client,
    TokenService                           issuer,
    ITokenStore<SchemataToken>             tokens,
    IOptions<SchemataAuthorizationOptions> options
) : IntrospectionEndpoint
    where TApp : SchemataApplication
{
    public override async Task<IntrospectionResponse> HandleAsync(
        IntrospectRequest                  request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Token)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.Token });
        }

        var ctx = AdviceContext.Require();

        var application = (await client.AuthenticateAsync(null, ClientAuthenticationForm.Build(
            request.ClientId, request.ClientSecret, request.ClientAssertion, request.ClientAssertionType),
            headers, ct, CanonicalIssuer.Combine(options.Value.Issuer, Endpoints.Introspect)))?.Application;

        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }

        var entity = await tokens.FindByReferenceIdAsync(request.Token, ct);
        if (string.IsNullOrWhiteSpace(entity?.Payload)) {
            return new() { Active = false };
        }

        var principal = await issuer.Validate(entity.Payload);
        var grant     = AuthorizationGrantContexts.Deserialize(entity.GrantContext);
        if (principal is null || grant is null) {
            return new() { Active = false };
        }

        // RFC 9068 §3: a token minted for several resource indicators carries one aud claim per
        // value, so introspection echoes all of them.
        var audiences = principal.FindAll(Claims.Audience).Select(c => c.Value).ToList();

        var introspection = new IntrospectionContext<TApp> {
            Application = application,
            Request     = request,
            Token       = entity,
            Principal   = principal,
            Response = new() {
                Active    = true,
                Scope     = principal.FindFirstValue(Claims.Scope),
                ClientId  = principal.FindFirstValue(Claims.ClientId),
                Username  = principal.FindFirstValue(Claims.Name),
                // RFC 7662 §2.2 requires token_type; the DPoP flow feature's advisor refines bound tokens.
                TokenType = Schemes.Bearer,
                Exp       = GetUnixTimestamp(principal, Claims.Expiration),
                Iat       = GetUnixTimestamp(principal, Claims.IssuedAt),
                Nbf       = GetUnixTimestamp(principal, Claims.NotBefore),
                Sub       = principal.FindFirstValue(IdentityClaims.Subject),
                Aud       = audiences.Count > 0 ? audiences : null,
                Iss       = principal.FindFirstValue(Claims.Issuer),
                Jti       = principal.FindFirstValue(Claims.JwtId),

                // RFC 9470 §6.2: the persisted original event supplies acr/auth_time; §6 defines no amr member.
                Acr      = grant.Authentication?.Acr,
                AuthTime = grant.Authentication?.AuthTime,
            },
        };

        switch (await Advisor.For<IIntrospectionAdvisor<TApp>>()
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
