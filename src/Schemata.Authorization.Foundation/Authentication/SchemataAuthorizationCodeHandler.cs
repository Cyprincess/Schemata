using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Authentication;

public class SchemataAuthorizationCodeHandler<TApp, TToken>(
    IOptionsMonitor<SchemataAuthenticationHandlerOptions> options,
    IOptions<SchemataAuthorizationOptions>                config,
    IOptions<JsonSerializerOptions>                       json,
    ILoggerFactory                                        logger,
    UrlEncoder                                            encoder,
    TokenService                                          issuer,
    IApplicationManager<TApp>                             apps,
    ITokenManager<TToken>                                 tokens
) : SignInAuthenticationHandler<SchemataAuthenticationHandlerOptions>(options, logger, encoder)
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() { throw new NotImplementedException(); }

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) {
        throw new NotImplementedException();
    }

    protected override async Task HandleSignInAsync(ClaimsPrincipal principal, AuthenticationProperties? properties) {
        var ct    = Context.RequestAborted;
        var items = properties?.Items ?? new Dictionary<string, string?>();
        var ctx   = new AdviceContext(Context.RequestServices);

        items.TryGetValue(Properties.Scope, out var scope);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);

        var claims = new List<Claim>();
        foreach (var identity in principal.Identities) {
            claims.AddRange(identity.Claims);
        }

        var client = principal.FindFirstValue(Claims.ClientId);
        var app = !string.IsNullOrWhiteSpace(client)
            ? (await apps.FindByCanonicalNameAsync(client, ct))?.Name
            : null;
        var subject = principal.FindFirstValue(Claims.Subject);

        switch (await Advisor.For<IClaimsAdvisor>().RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TokenResponse>(out var _):
                // Authorization endpoint does not return a JSON token response;
                // fall through to continue processing claims for redirect/form_post.
                break;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.AccessDenied,
                                         SchemataResources.GetResourceString(SchemataResources.ST4008));
        }

        for (var i = claims.Count - 1; i >= 0; i--) {
            var destinations = new HashSet<string>();

            switch (await Advisor.For<IDestinationAdvisor>().RunAsync(ctx, claims[i], destinations, principal, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                    break;
                case AdviseResult.Block:
                default:
                    claims.RemoveAt(i);
                    continue;
            }

            if (destinations.Count == 0) {
                claims.RemoveAt(i);
            } else {
                foreach (var d in destinations) {
                    claims[i].Properties[d] = Parameters.Token;
                }
            }
        }

        var access = claims.Where(c => c.Properties.ContainsKey(ClaimDestinations.AccessToken)).ToList();
        var id     = claims.Where(c => c.Properties.ContainsKey(ClaimDestinations.IdentityToken)).ToList();

        if (!string.IsNullOrWhiteSpace(scope)) {
            access.Add(new(Claims.Scope, scope));
        }

        if (!string.IsNullOrWhiteSpace(sid)) {
            access.Add(new(Claims.SessionId, sid));
            id.Add(new(Claims.SessionId, sid));
        }

        items.TryGetValue(Properties.ResponseType, out var type);
        var types = type!.Split(' ');

        items.TryGetValue(Properties.RedirectUri, out var redirectUri);
        items.TryGetValue(Properties.ResponseMode, out var responseMode);

        var parameters = new Dictionary<string, string?>();

        items.TryGetValue(Properties.State, out var state);
        if (!string.IsNullOrWhiteSpace(state)) parameters[Parameters.State]            = state;
        if (!string.IsNullOrWhiteSpace(config.Value.Issuer)) parameters[Claims.Issuer] = config.Value.Issuer;

        string? at = null;
        if (types.Contains(ResponseTypes.Token)) {
            at = await SchemataAuthenticationHandler<TApp, TToken>.CreateTokenAsync(tokens, issuer, access, config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken, subject, app, authorizationName, sid, ct);
            parameters[Parameters.AccessToken] = at;
            parameters[Parameters.TokenType]   = Schemes.Bearer;
            parameters[Parameters.ExpiresIn]   = ((int)config.Value.AccessTokenLifetime.TotalSeconds).ToString();
        }

        if (types.Contains(ResponseTypes.Code)) {
            parameters[Parameters.Code] = await CreateAuthorizationCodeAsync(client, scope, type, subject, app, items, ct);
        }

        if (types.Contains(ResponseTypes.IdToken)
         && ScopeParser.Contains(scope, Scopes.OpenId)
         && SchemataAuthenticationHandler<TApp, TToken>.IsUserGrant(items)) {
            parameters[Parameters.IdToken] = SchemataAuthenticationHandler<TApp, TToken>.CreateIdToken(issuer, items, id, config.Value.IdTokenLifetime, at, parameters.GetValueOrDefault(Parameters.Code));
        }

        var actionResult = ResponseModeService.CreateCallback(redirectUri!, parameters, responseMode!);
        var routeData    = Context.GetRouteData();
        await actionResult.ExecuteResultAsync(new(Context, routeData, new()));
    }

    private async Task<string> CreateAuthorizationCodeAsync(
        string?                      client,
        string?                      scope,
        string?                      type,
        string?                      subject,
        string?                      appName,
        IDictionary<string, string?> items,
        CancellationToken            ct
    ) {
        items.TryGetValue(Properties.RedirectUri, out var redirect);
        items.TryGetValue(Properties.Nonce, out var nonce);
        items.TryGetValue(Properties.CodeChallenge, out var challenge);
        items.TryGetValue(Properties.CodeChallengeMethod, out var method);
        items.TryGetValue(Properties.MaxAge, out var maxAge);
        items.TryGetValue(Properties.AuthTime, out var authTime);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);

        var payload = new AuthorizeRequest {
            ClientId            = client,
            RedirectUri         = redirect,
            Scope               = scope,
            Nonce               = nonce,
            ResponseType        = type,
            CodeChallenge       = challenge,
            CodeChallengeMethod = method,
            MaxAge              = maxAge,
            AuthTime            = authTime,
        };

        var reference = issuer.CreateReference();
        var now       = DateTime.UtcNow;
        var entity = new TToken {
            Type              = TokenTypes.AuthorizationCode,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = JsonSerializer.Serialize(payload, json.Value),
            Subject           = subject,
            ExpireTime        = now + config.Value.AuthorizationCodeLifetime,
            ApplicationName   = appName,
            AuthorizationName = authorizationName,
            SessionId         = sid,
            CreateTime        = now,
        };
        await tokens.CreateAsync(entity, ct);

        return reference;
    }
}
