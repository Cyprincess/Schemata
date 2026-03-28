using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
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

public class SchemataAuthenticationHandler<TApp, TToken>(
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
    public static bool IsUserGrant(IDictionary<string, string?> items) {
        items.TryGetValue(Properties.GrantType, out var grant);
        return grant is GrantTypes.AuthorizationCode or GrantTypes.RefreshToken or GrantTypes.TokenExchange;
    }

    public static bool ShouldIssueRefreshToken(IDictionary<string, string?> items) {
        if (!items.TryGetValue(Properties.GrantType, out var grant) || string.IsNullOrWhiteSpace(grant)) {
            return false;
        }

        switch (grant) {
            case GrantTypes.RefreshToken:
                return true;
            case GrantTypes.ClientCredentials:
                return false;
            default:
                items.TryGetValue(Properties.Scope, out var scope);
                return ScopeParser.Contains(scope, Scopes.OfflineAccess);
        }
    }

    public static string CreateIdToken(
        TokenService                 token,
        IDictionary<string, string?> items,
        List<Claim>                  claims,
        TimeSpan                     lifetime,
        string?                      at,
        string?                      code
    ) {
        items.TryGetValue(Properties.Nonce, out var nonce);
        items.TryGetValue(Properties.MaxAge, out var maxAge);

        if (string.IsNullOrWhiteSpace(maxAge)) {
            return token.CreateIdToken(claims, lifetime, at, code, nonce);
        }

        items.TryGetValue(Properties.AuthTime, out var authTime);
        if (!string.IsNullOrWhiteSpace(authTime)) {
            claims.Add(new(Claims.AuthTime, authTime));
        }

        return token.CreateIdToken(claims, lifetime, at, code, nonce);
    }

    public static async Task<string> CreateTokenAsync(
        ITokenManager<TToken> tokens,
        TokenService          token,
        List<Claim>           claims,
        string?               format,
        TimeSpan              lifetime,
        string                type,
        string?               subject,
        string?               application,
        string?               authorization,
        string?               session,
        CancellationToken     ct
    ) {
        var jti = Guid.NewGuid().ToString("N");
        claims.Add(new(Claims.JwtId, jti));

        string value;
        string reference;

        switch (format) {
            case TokenFormats.Jwt:
                value     = token.CreateToken(claims, lifetime);
                reference = value;
                break;

            case TokenFormats.Jwe:
                value     = token.CreateToken(claims, lifetime, true);
                reference = value;
                break;

            case TokenFormats.Reference:
            default:
                reference = token.CreateReference();
                value     = reference;
                break;
        }

        var payload = format == TokenFormats.Reference ? token.CreateToken(claims, lifetime) : value;

        var now = DateTime.UtcNow;
        var entity = new TToken {
            Type              = type,
            Format            = format,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = payload,
            Subject           = subject,
            ExpireTime        = now + lifetime,
            CreateTime        = now,
            ApplicationName   = application,
            AuthorizationName = authorization,
            SessionId         = session,
        };
        await tokens.CreateAsync(entity, ct);

        return value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        var ct = Context.RequestAborted;

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(Schemes.Bearer + " ", StringComparison.OrdinalIgnoreCase)) {
            return AuthenticateResult.NoResult();
        }

        var token = header[(Schemes.Bearer + " ").Length..].Trim();
        if (string.IsNullOrWhiteSpace(token)) {
            return AuthenticateResult.NoResult();
        }

        var entity = await tokens.FindByReferenceIdAsync(token, ct);
        if (string.IsNullOrWhiteSpace(entity?.ApplicationName)
         || entity.Type != TokenTypes.AccessToken
         || entity.Status != TokenStatuses.Valid) {
            return AuthenticateResult.NoResult();
        }

        var principal = entity.Format switch {
            TokenFormats.Reference when !string.IsNullOrWhiteSpace(entity.Payload) => await issuer.Validate(entity.Payload, entity.ApplicationName),
            TokenFormats.Jwt or TokenFormats.Jwe => await issuer.Validate(token, entity.ApplicationName),
            var _                                => null,
        };

        if (principal is null) {
            return AuthenticateResult.NoResult();
        }

        var id = principal.Identity as ClaimsIdentity;
        var claims = id!.Claims.Where(c => c.Type != Claims.Subject)
                        .Append(new(Claims.Subject, entity.Subject ?? string.Empty))
                        .ToList();
        principal = new(new ClaimsIdentity(claims, id.AuthenticationType, Claims.Subject, Claims.Role));

        return AuthenticateResult.Success(new(principal, Scheme.Name));
    }

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) { return Task.CompletedTask; }

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
        var @internal = principal.FindFirstValue(Claims.Subject);

        switch (await Advisor.For<IClaimsAdvisor>().RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TokenResponse>(out var handled):
                Context.Response.ContentType = MediaTypeNames.Application.Json;
                await JsonSerializer.SerializeAsync(Context.Response.Body, handled, json.Value, Context.RequestAborted);

                return;
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

        var at = await CreateTokenAsync(tokens, issuer, access, config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken, @internal, app, authorizationName, sid, ct);

        var response = new TokenResponse {
            AccessToken = at,
            TokenType   = Schemes.Bearer,
            ExpiresIn   = (int)config.Value.AccessTokenLifetime.TotalSeconds,
            Scope       = scope,
        };

        if (ShouldIssueRefreshToken(items)) {
            response.RefreshToken = await CreateTokenAsync(tokens, issuer, [..access], config.Value.RefreshTokenFormat, config.Value.RefreshTokenLifetime, TokenTypes.RefreshToken, @internal, app, authorizationName, sid, ct);
        }

        if (ScopeParser.Contains(scope, Scopes.OpenId) && IsUserGrant(items)) {
            response.IdToken = CreateIdToken(issuer, items, id, config.Value.IdTokenLifetime, response.AccessToken, null);
        }

        if (items.TryGetValue(Properties.IssuedTokenType, out var issuedType)
         && !string.IsNullOrWhiteSpace(issuedType)) {
            response.IssuedTokenType = issuedType;
        }

        Context.Response.ContentType = MediaTypeNames.Application.Json;
        await JsonSerializer.SerializeAsync(Context.Response.Body, response, json.Value, Context.RequestAborted);
    }
}
