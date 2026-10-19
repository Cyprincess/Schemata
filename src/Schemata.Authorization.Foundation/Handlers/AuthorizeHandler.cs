using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

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
public sealed class AuthorizeHandler<TApp, TSecurity, TToken>(
    Schemata.Authorization.Skeleton.Managers.IApplicationManager<TApp> applications,
    ITokenStore<TToken>                    tokens,
    TokenService<TSecurity>                issuer,
    IOptions<SchemataAuthorizationOptions> options,
    IOptions<JsonSerializerOptions>        json,
    TimeProvider?                          time = null
) : AuthorizeEndpoint
    where TApp : SchemataApplication
    where TSecurity : SchemataSecurity, new()
    where TToken : SchemataToken, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public override async Task<AuthorizationResult> AuthorizeAsync(
        AuthorizeRequest  request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct
    ) {
        var authz = new AuthorizeContext<TApp> {
            Request   = request,
            Principal = principal,
        };

        ClearServerFields(request);

        // Central response finalization: once the request advisors have normalized PAR/JAR input
        // and the client/redirect advisor has validated the redirect, every later failure —
        // advisor throws (PKCE, grant, scope, response-type policy) and block results alike —
        // inherits the validated request facts here instead of decorating each throw site.
        AdviseResult stage;
        try {
            stage = await Advisor.For<IAuthorizeRequestAdvisor<TApp>>().RunAsync(context, authz, ct);
        } catch (OAuthException ex) {
            throw ex.WithCallback(authz);
        }

        switch (stage) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when authz.Result is { } normalized:
                return normalized;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST).WithCallback(authz);
        }

        // PAR/JAR normalization may have replaced the request object; server-owned fields never
        // survive from client input, and an uninstalled RAR capability cannot leak the raw parameter.
        if (authz.Request is null) {
            throw new InvalidOperationException("The authorization request is missing after request normalization.");
        }

        ClearServerFields(authz.Request);
        if (context.ServiceProvider.GetService<AuthorizationDetailsService>() is null) {
            authz.Request.AuthorizationDetails = null;
        }

        try {
            stage = await Advisor.For<IAuthorizeAdvisor<TApp>>().RunAsync(context, authz, ct);
        } catch (OAuthException ex) {
            throw ex.WithCallback(authz);
        }

        switch (stage) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when authz.Result is { } endpoint:
                return endpoint;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.AccessDenied, SchemataResources.ACCESS_DENIED).WithCallback(authz);
        }

        if (authz.Application is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS).WithCallback(authz);
        }

        if (!authz.RequireReauthentication && authz.Request.Authentication is not null) {
            authz.Request.AuthenticationSubject = authz.Principal?.FindFirstValue(SchemataConstants.IdentityClaims.Subject);
        } else {
            // A step-up/account-selection continuation resolves the authentication event from the
            // approving principal; pre-login evidence is never persisted as the new event.
            authz.Request.Authentication        = null;
            authz.Request.AuthenticationSubject = null;
        }
        authz.Request.AuthenticationRequiredAfter = authz.RequireReauthentication
            ? _time.GetUtcNow().ToUnixTimeSeconds()
            : null;



        var reference = issuer.CreateReference();
        var payload   = JsonSerializer.Serialize(authz.Request, json.Value);

        var interaction = new TToken {
            Application = authz.Application.CanonicalName,
            Type        = TokenTypes.Interaction,
            Status      = TokenStatuses.Valid,
            ReferenceId = reference,
            Payload     = payload,
            ExpireTime  = _time.GetUtcNow().UtcDateTime + options.Value.InteractionTokenLifetime,
        };

        await tokens.CreateAsync(interaction, ct,
            (transaction, cancellation) => applications.EnlistTokenPublicationAsync(transaction, [interaction], cancellation));

        var query = QueryString.Create(new Dictionary<string, string?> {
            { Parameters.Code, reference },
            { Parameters.CodeType, TokenTypeUris.Interaction },
        });

        return AuthorizationResult.Redirect($"{options.Value.InteractionUri}{query.ToUriComponent()}");
    }

    private static void ClearServerFields(AuthorizeRequest request) {
        request.OpSessionId                 = null;
        request.OpSessionSubject            = null;
        request.SessionStateSalt            = null;
        request.Authentication              = null;
        request.AuthenticationSubject       = null;
        request.AuthenticationRequiredAfter = null;
        request.GrantProfile                = null;
    }
}
