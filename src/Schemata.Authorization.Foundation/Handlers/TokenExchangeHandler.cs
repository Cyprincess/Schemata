using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>urn:ietf:params:oauth:grant-type:token-exchange</c> grant type.
///     Authenticates the client, runs the <see cref="ITokenRequestAdvisor{TApp}" />
///     pipeline, then delegates to a keyed <see cref="ITokenExchangeHandler{TApp}" />
///     resolved by <c>subject_token_type</c>,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
/// </summary>
public sealed class TokenExchangeHandler<TApp>(IClientAuthenticationService<TApp> client, IServiceProvider sp) : IGrantHandler
    where TApp : SchemataApplication
{
    #region IGrantHandler Members

    /// <inheritdoc cref="IGrantHandler.GrantType" />
    public string GrantType => GrantTypes.TokenExchange;

    /// <summary>
    ///     Processes a token exchange request by authenticating the client,
    ///     running the advisor pipeline, and routing to the appropriate
    ///     <see cref="ITokenExchangeHandler{TApp}" /> based on the
    ///     <see cref="TokenRequest.SubjectTokenType" />.
    /// </summary>
    /// <param name="request">Token exchange request.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.SubjectToken)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.SubjectToken)
            );
        }

        if (string.IsNullOrWhiteSpace(request.SubjectTokenType)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(
                    SchemataResources.GetResourceString(SchemataResources.ST1013),
                    Parameters.SubjectTokenType
                )
            );
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
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
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.ST4001)
                );
        }

        var handler = sp.GetKeyedService<ITokenExchangeHandler<TApp>>(request.SubjectTokenType);
        if (handler is null) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(
                    SchemataResources.GetResourceString(SchemataResources.ST1015),
                    Parameters.SubjectTokenType
                )
            );
        }

        return await handler.HandleAsync(application, request, null, ct);
    }

    #endregion
}
