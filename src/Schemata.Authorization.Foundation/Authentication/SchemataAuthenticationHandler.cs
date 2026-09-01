using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     ASP.NET Core authentication handler for the Schemata Bearer token scheme. Bearer
///     authentication validates stored access tokens. Direct compatibility sign-in calls delegate
///     issuance to <see cref="IAuthorizationSignInService" /> and HTTP writing to
///     <see cref="IAuthorizationSignInHttpWriter" />.
/// </summary>
public class SchemataAuthenticationHandler<TApp, TToken>(
    IOptionsMonitor<SchemataAuthenticationHandlerOptions> options,
    ILoggerFactory                                        logger,
    UrlEncoder                                            encoder,
    TokenService                                          issuer,
    ITokenManager<TToken>                                 tokens,
    IAuthorizationSignInService                           signIns,
    IAuthorizationSignInHttpWriter                        writer
) : SignInAuthenticationHandler<SchemataAuthenticationHandlerOptions>(options, logger, encoder)
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
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
        var jti         = Guid.NewGuid().ToString("n");
        var tokenClaims = new List<Claim>(claims) { new(Claims.JwtId, jti) };

        string value;
        string reference;

        switch (format) {
            case TokenFormats.Jwt:
                value     = token.CreateToken(tokenClaims, lifetime);
                reference = value;
                break;

            case TokenFormats.Jwe:
                value     = token.CreateToken(tokenClaims, lifetime, true);
                reference = value;
                break;

            case TokenFormats.Reference:
            default:
                reference = token.CreateReference();
                value     = reference;
                break;
        }

        var payload = format == TokenFormats.Reference ? token.CreateToken(tokenClaims, lifetime) : value;

        var now = time.GetUtcNow().UtcDateTime;
        var entity = new TToken {
            Name              = jti,
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

        if (principal.Identity is not ClaimsIdentity id) {
            return AuthenticateResult.NoResult();
        }

        var claims = id.Claims.Where(c => c.Type != IdentityClaims.Subject)
                       .Append(new(IdentityClaims.Subject, entity.Subject ?? string.Empty))
                       .ToList();
        principal = new(new ClaimsIdentity(claims, id.AuthenticationType, IdentityClaims.Subject, IdentityClaims.Role));

        return AuthenticateResult.Success(new(principal, Scheme.Name));
    }

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) { return Task.CompletedTask; }

    protected override async Task HandleSignInAsync(
        ClaimsPrincipal          principal,
        AuthenticationProperties? properties
    ) {
        var response = await signIns.IssueAsync(
            principal, properties?.Items, AuthorizationSignInResponseKind.Token, Context.RequestAborted);
        await writer.WriteAsync(Context, response, Context.RequestAborted);
    }
}
