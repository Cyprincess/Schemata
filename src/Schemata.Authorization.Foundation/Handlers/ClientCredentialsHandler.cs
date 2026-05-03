using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>client_credentials</c> grant type.
///     Authenticates the client, runs the <see cref="ITokenRequestAdvisor{TApp}" /> pipeline,
///     and emits a <see cref="AuthorizationResult.SignIn" /> with the client_id claim.
///     No user subject is associated — the client is the resource owner,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.1">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.1
///     </seealso>
///     .
/// </summary>
public sealed class ClientCredentialsHandler<TApp>(IClientAuthenticationService<TApp> client, IServiceProvider sp) : IGrantHandler
    where TApp : SchemataApplication
{
    #region IGrantHandler Members

    /// <inheritdoc cref="IGrantHandler.GrantType" />
    public string GrantType => GrantTypes.ClientCredentials;

    /// <summary>
    ///     Issues tokens on behalf of a confidential client using client credentials.
    /// </summary>
    /// <param name="request">Token request containing client credentials.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        var ctx = new AdviceContext(sp);

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001));
        }

        var claims = new List<Claim> {
            new(Claims.ClientId, application.ClientId),
        };

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType] = GrantTypes.ClientCredentials,
            [Properties.Scope]     = request.Scope,
        });
    }

    #endregion
}
