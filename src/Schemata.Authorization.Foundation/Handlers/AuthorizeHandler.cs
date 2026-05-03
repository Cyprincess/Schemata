using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OAuth 2.0 Authorization Endpoint.
///     Runs the <see cref="IAuthorizeAdvisor{TApp}" /> pipeline,
///     then redirects unauthenticated users to the interaction URI with a short-lived
///     interaction token that encodes the original <see cref="AuthorizeRequest" />,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9700.html#section-2.1.2">
///         RFC 9700: The OAuth 2.0 Authorization
///         Framework: Best Current Practice §2.1.2
///     </seealso>
///     .
/// </summary>
public sealed class AuthorizeHandler<TApp, TToken>(
    ITokenManager<TToken>                  tokens,
    TokenService                           issuer,
    IOptions<SchemataAuthorizationOptions> options,
    IServiceProvider                       sp,
    IOptions<JsonSerializerOptions>        json
) : AuthorizeEndpoint
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    /// <summary>
    ///     Processes an authorization request by running the advisor pipeline and,
    ///     when interaction is required, creating a reference-based interaction token
    ///     that carries the serialized <see cref="AuthorizeRequest" /> to the consent/login SPA.
    /// </summary>
    /// <param name="request">The validated OAuth 2.0 authorization request.</param>
    /// <param name="principal">The current authenticated principal, or empty if anonymous.</param>
    /// <param name="ct">Cancellation token.</param>
    public override async Task<AuthorizationResult> AuthorizeAsync(
        AuthorizeRequest  request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    ) {
        var ctx = new AdviceContext(sp);
        var authz = new AuthorizeContext<TApp> {
            Request      = request,
            Principal    = principal,
            ResponseMode = ResponseModeService.ResolveMode(request.ResponseMode, request.ResponseType),
        };

        switch (await Advisor.For<IAuthorizeAdvisor<TApp>>()
                             .RunAsync(ctx, authz, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var endpoint):
                return endpoint!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.AccessDenied, SchemataResources.GetResourceString(SchemataResources.ST4008)) {
                    RedirectUri  = authz.Request.RedirectUri,
                    State        = authz.Request.State,
                    ResponseMode = authz.ResponseMode,
                };
        }

        if (authz.Application is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.GetResourceString(SchemataResources.ST4001)) {
                RedirectUri  = authz.Request.RedirectUri,
                State        = authz.Request.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        if (authz.RequireReauthentication) {
            return AuthorizationResult.Challenge();
        }

        if (string.IsNullOrWhiteSpace(options.Value.InteractionUri)) {
            throw new OAuthException(OAuthErrors.ServerError, SchemataResources.GetResourceString(SchemataResources.ST4008)) {
                RedirectUri  = authz.Request.RedirectUri,
                State        = authz.Request.State,
                ResponseMode = authz.ResponseMode,
            };
        }

        var reference = issuer.CreateReference();
        var payload   = JsonSerializer.Serialize(authz.Request, json.Value);

        var interaction = new TToken {
            ApplicationName = authz.Application.Name,
            Type            = TokenTypes.Interaction,
            Status          = TokenStatuses.Valid,
            ReferenceId     = reference,
            Payload         = payload,
            ExpireTime      = DateTime.UtcNow + options.Value.InteractionTokenLifetime,
        };

        await tokens.CreateAsync(interaction, ct);

        var query = QueryString.Create(new Dictionary<string, string?> {
            { Parameters.Code, reference },
            { Parameters.CodeType, TokenTypeUris.Interaction },
        });

        return AuthorizationResult.Redirect($"{options.Value.InteractionUri}{query.ToUriComponent()}");
    }
}
