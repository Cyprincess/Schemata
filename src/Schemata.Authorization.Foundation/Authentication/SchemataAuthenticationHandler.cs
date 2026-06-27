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
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     ASP.NET Core authentication handler for the Schemata Bearer token scheme.
///     Handles three concerns:
///     <list type="number">
///         <item>
///             <description>
///                 Bearer token validation: extracts the token from the
///                 <c>Authorization: Bearer</c> header, looks up the stored entity,
///                 validates the JWT/JWE/opaque payload, and returns the claims principal.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Token issuance (<c>SignIn</c>): runs claim resolution and destination
///                 advisor pipelines, issues access tokens, refresh tokens, and ID tokens,
///                 and writes a JSON <see cref="TokenResponse" /> to the HTTP response.
///             </description>
///         </item>
///     </list>
/// </summary>
public class SchemataAuthenticationHandler<TApp, TToken>(
    IOptionsMonitor<SchemataAuthenticationHandlerOptions> options,
    IOptions<SchemataAuthorizationOptions>                config,
    IOptions<JsonSerializerOptions>                       json,
    ILoggerFactory                                        logger,
    UrlEncoder                                            encoder,
    TokenService                                          issuer,
    IApplicationManager<TApp>                             apps,
    ITokenManager<TToken>                                 tokens,
    TimeProvider?                                         time = null
) : SignInAuthenticationHandler<SchemataAuthenticationHandlerOptions>(options, logger, encoder)
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    ///     Returns <c>true</c> when the grant type indicates a user-present flow
    ///     that can receive an ID token.
    /// </summary>
    public static bool IsUserGrant(IDictionary<string, string?> items) {
        items.TryGetValue(Properties.GrantType, out var grant);
        return grant is GrantTypes.AuthorizationCode or GrantTypes.RefreshToken or GrantTypes.TokenExchange;
    }

    /// <summary>
    ///     Determines whether a refresh token should be issued.
    ///     Returns <c>true</c> for the <c>refresh_token</c> grant (rotation),
    ///     <c>false</c> for <c>client_credentials</c>, and otherwise follows
    ///     the presence of the <c>offline_access</c> scope.
    /// </summary>
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

    /// <summary>
    ///     Creates a signed OIDC ID token (JWT) with optional <c>at_hash</c>,
    ///     <c>c_hash</c>, and <c>nonce</c> claims.  When <c>max_age</c> and
    ///     <c>auth_time</c> are present, the <c>auth_time</c> claim is included
    ///     in the token.
    /// </summary>
    /// <param name="token">The <see cref="TokenService" /> used for signing.</param>
    /// <param name="items">Authentication properties dictionary.</param>
    /// <param name="claims">Claims to include in the ID token.</param>
    /// <param name="lifetime">ID token validity duration.</param>
    /// <param name="at">Access token value for <c>at_hash</c> computation.</param>
    /// <param name="code">Authorization code value for <c>c_hash</c> computation.</param>
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

    /// <summary>
    ///     Creates and persists a token entity (access, refresh, or ID).
    ///     For JWT/JWE formats, the reference IS the encoded token value;
    ///     for opaque reference tokens, a separate random reference is generated
    ///     and the JWT is stored as the payload for later introspection.
    ///     Returns the value that should be emitted to the client.
    /// </summary>
    /// <param name="tokens">Token storage manager.</param>
    /// <param name="token">Token service for JWT/JWE creation.</param>
    /// <param name="claims">Claims to embed.</param>
    /// <param name="format">Token serialization format (JWT, JWE, or Reference).</param>
    /// <param name="lifetime">Token validity duration.</param>
    /// <param name="type">Token type (e.g., <see cref="TokenTypes.AccessToken" />).</param>
    /// <param name="subject">Resource owner subject.</param>
    /// <param name="application">Issuing client application name.</param>
    /// <param name="authorization">Linked authorization/consent record name.</param>
    /// <param name="session">OP session identifier.</param>
    /// <param name="time">Clock for the token's create and expiry timestamps.</param>
    /// <param name="ct">A cancellation token.</param>
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
        TimeProvider          time,
        CancellationToken     ct
    ) {
        var jti = Identifiers.NewUid().ToString("n");
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

        var now = time.GetUtcNow().UtcDateTime;
        var entity = new TToken {
            Type              = type,
            Format            = format,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = payload,
            Subject           = subject,
            ExpireTime        = now + lifetime,
            Application       = application,
            Authorization     = authorization,
            SessionId         = session,
        };
        await tokens.CreateAsync(entity, ct);

        return value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
        var ct = Context.RequestAborted;

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)
         || !header.StartsWith(Schemes.Bearer + " ", StringComparison.OrdinalIgnoreCase)) {
            return AuthenticateResult.NoResult();
        }

        var token = header[(Schemes.Bearer + " ").Length..].Trim();
        if (string.IsNullOrWhiteSpace(token)) {
            return AuthenticateResult.NoResult();
        }

        var entity = await tokens.FindByReferenceIdAsync(token, ct);
        if (string.IsNullOrWhiteSpace(entity?.Application)
         || entity.Type != TokenTypes.AccessToken
         || entity.Status != TokenStatuses.Valid) {
            return AuthenticateResult.NoResult();
        }

        var principal = entity.Format switch {
            TokenFormats.Reference when !string.IsNullOrWhiteSpace(entity.Payload) => await issuer.Validate(entity.Payload, entity.Application),
            TokenFormats.Jwt or TokenFormats.Jwe => await issuer.Validate(token, entity.Application),
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

        if (principal.Identity is not ClaimsIdentity identity) {
            return;
        }

        items.TryGetValue(Properties.Scope, out var scope);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);

        if (!string.IsNullOrWhiteSpace(scope)) {
            identity.AddClaim(new(Claims.Scope, scope));
        }

        if (!string.IsNullOrWhiteSpace(sid)) {
            identity.AddClaim(new(Claims.SessionId, sid));
        }

        var claims = new List<Claim>();
        claims.AddRange(identity.Claims);

        var client = principal.FindFirstValue(Claims.ClientId);
        // SchemataToken.Application carries the AIP-122 canonical name; the OAuth wire client_id
        // is still resolved against SchemataApplication.Name via FindByClientIdAsync.
        var app = !string.IsNullOrWhiteSpace(client)
            ? (await apps.FindByClientIdAsync(client, ct))?.CanonicalName
            : null;
        var @internal = principal.FindFirstValue(Claims.Subject);

        switch (await Advisor.For<IClaimsAdvisor>()
                             .RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TokenResponse>(out var handled):
                Context.Response.ContentType = MediaTypeNames.Application.Json;
                await JsonSerializer.SerializeAsync(Context.Response.Body, handled, json.Value, Context.RequestAborted);

                return;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ACCESS_DENIED)
                );
        }

        foreach (var claim in claims) {
            var destinations = new HashSet<string>();

            switch (await Advisor.For<IDestinationAdvisor>()
                                 .RunAsync(ctx, claim, destinations, principal, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                    break;
                case AdviseResult.Block:
                default:
                    continue;
            }

            if (destinations.Count == 0) {
                continue;
            }

            foreach (var d in destinations) {
                claim.Properties[d] = Parameters.Token;
            }
        }

        var access = claims.Where(c => c.Properties.ContainsKey(ClaimDestinations.AccessToken)).ToList();
        var id     = claims.Where(c => c.Properties.ContainsKey(ClaimDestinations.IdentityToken)).ToList();

        var at = await CreateTokenAsync(tokens, issuer, access, config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken, @internal, app, authorizationName, sid, _time, ct);

        var response = new TokenResponse {
            AccessToken = at,
            TokenType   = Schemes.Bearer,
            ExpiresIn   = (int)config.Value.AccessTokenLifetime.TotalSeconds,
            Scope       = scope,
        };

        if (ShouldIssueRefreshToken(items)) {
            response.RefreshToken = await CreateTokenAsync(tokens, issuer, [..access], config.Value.RefreshTokenFormat, config.Value.RefreshTokenLifetime, TokenTypes.RefreshToken, @internal, app, authorizationName, sid, _time, ct);
        }

        // OIDC Core §3.1.3.7: ID tokens are only returned when openid is in scope
        // and a user is present (not client_credentials).
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
