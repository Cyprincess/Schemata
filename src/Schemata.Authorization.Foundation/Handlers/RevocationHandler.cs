using System.Collections.Generic;
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
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
public sealed class RevocationHandler<TApp>(
    IClientAuthenticationService<TApp>     client,
    ITokenStore<SchemataToken>             tokens,
    IOptions<SchemataAuthorizationOptions> options
) : RevocationEndpoint
    where TApp : SchemataApplication
{
    public override async Task HandleAsync(
        RevokeRequest                      request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.Token)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.Token });
        }

        // RFC 7009 §2.2: an invalid or unknown token_type_hint value is ignored by the
        // authorization server and does not influence the revocation response. The hint is only a
        // search optimization; the lookup below already searches across all supported token types.
        var ctx = AdviceContext.Require();

        var application = (await client.AuthenticateAsync(null, ClientAuthenticationForm.Build(
            request.ClientId, request.ClientSecret, request.ClientAssertion, request.ClientAssertionType),
            headers, ct, CanonicalIssuer.Combine(options.Value.Issuer, Endpoints.Revoke)))?.Application;

        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            return;
        }

        var entity = await tokens.FindByReferenceIdAsync(request.Token, ct);
        if (entity is null) {
            return;
        }

        switch (await Advisor.For<IRevocationAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, entity, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return;
            case AdviseResult.Block:
            default:
                return;
        }

        if (entity.Type == TokenTypes.RefreshToken && !string.IsNullOrWhiteSpace(entity.Family)) {
            await tokens.InvalidateFamilyAsync(entity.Family, ct);
        } else {
            await tokens.RevokeAsync(entity, ct);
        }
    }
}
