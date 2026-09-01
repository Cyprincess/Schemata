using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Transport-neutral OAuth/OIDC callback parameters.</summary>
public sealed record AuthorizationCallbackResponse(
    string                       RedirectUri,
    Dictionary<string, string?> Parameters,
    string?                      ResponseMode
);

/// <summary>Issued token or authorization callback returned to an HTTP edge.</summary>
public sealed record AuthorizationSignInResponse(
    TokenResponse?                 Token,
    AuthorizationCallbackResponse? Callback
);

/// <summary>Selects token issuance or authorization callback issuance.</summary>
public enum AuthorizationSignInResponseKind
{
    Token,
    Callback,
}

/// <summary>Issues protocol responses from an authorized principal without writing HTTP state.</summary>
public interface IAuthorizationSignInService
{
    Task<AuthorizationSignInResponse> IssueAsync(
        ClaimsPrincipal                       principal,
        IDictionary<string, string?>?          properties,
        AuthorizationSignInResponseKind        kind,
        CancellationToken                     ct = default);
}

/// <summary>Default transport-neutral sign-in issuer.</summary>
/// <typeparam name="TApp">Application entity type.</typeparam>
/// <typeparam name="TToken">Token entity type.</typeparam>
public sealed class AuthorizationSignInService<TApp, TToken>(
    IOptions<SchemataAuthorizationOptions> config,
    IOptions<JsonSerializerOptions>        json,
    TokenService                           issuer,
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    IServiceProvider                       services,
    TimeProvider?                          time = null
) : IAuthorizationSignInService
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<AuthorizationSignInResponse> IssueAsync(
        ClaimsPrincipal                       principal,
        IDictionary<string, string?>?          properties,
        AuthorizationSignInResponseKind        kind,
        CancellationToken                     ct = default
    ) {
        ArgumentNullException.ThrowIfNull(principal);
        var items = properties is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(properties);
        var callback = kind == AuthorizationSignInResponseKind.Callback;
        var ctx = AdviceContext.Current;
        using var ambient = ctx is null ? AdviceContext.Establish(ctx = new AdviceContext(services)) : null;

        if (principal.Identity is not ClaimsIdentity identity) {
            throw new InvalidOperationException(
                "Authorization sign-in service requires a principal with a ClaimsIdentity.");
        }

        items.TryGetValue(Properties.Scope, out var scope);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);
        if (!string.IsNullOrWhiteSpace(scope)) identity.AddClaim(new(Claims.Scope, scope));
        if (!string.IsNullOrWhiteSpace(sid)) identity.AddClaim(new(Claims.SessionId, sid));

        var claims = identity.Claims.ToList();
        var client = principal.FindFirstValue(Claims.ClientId);
        var app = !string.IsNullOrWhiteSpace(client)
            ? (await apps.FindByClientIdAsync(client, ct))?.CanonicalName
            : null;
        var subject = principal.FindFirstValue(IdentityClaims.Subject);

        switch (await Advisor.For<IClaimsAdvisor>().RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when !callback && ctx.TryGet<TokenResponse>(out var handled):
                return new(handled, null);
            case AdviseResult.Handle:
                break;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ACCESS_DENIED));
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

            foreach (var destination in destinations) {
                claim.Properties[destination] = Parameters.Token;
            }
        }

        var access = claims.Where(claim => claim.Properties.ContainsKey(ClaimDestinations.AccessToken)).ToList();
        var id = claims.Where(claim => claim.Properties.ContainsKey(ClaimDestinations.IdentityToken)).ToList();
        return callback
            ? new(null, await IssueCallbackAsync(
                client, scope, subject, app, authorizationName, sid, items, access, id, ct))
            : new(await IssueTokenAsync(subject, app, authorizationName, sid, scope, items, access, id, ct), null);
    }

    private async Task<TokenResponse> IssueTokenAsync(
        string?                      subject,
        string?                      app,
        string?                      authorizationName,
        string?                      sid,
        string?                      scope,
        IDictionary<string, string?> items,
        List<Claim>                  access,
        List<Claim>                  id,
        CancellationToken            ct
    ) {
        var at = await SchemataAuthenticationHandler<TApp, TToken>.CreateTokenAsync(
            tokens, issuer, access,
            config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken,
            subject, app, authorizationName, sid, _time, ct);
        var response = new TokenResponse {
            AccessToken = at,
            TokenType   = Schemes.Bearer,
            ExpiresIn   = (int)config.Value.AccessTokenLifetime.TotalSeconds,
            Scope       = scope,
        };

        if (SchemataAuthenticationHandler<TApp, TToken>.ShouldIssueRefreshToken(items)) {
            response.RefreshToken = await SchemataAuthenticationHandler<TApp, TToken>.CreateTokenAsync(
                tokens, issuer, [..access],
                config.Value.RefreshTokenFormat, config.Value.RefreshTokenLifetime, TokenTypes.RefreshToken,
                subject, app, authorizationName, sid, _time, ct);
        }

        if (ScopeParser.Contains(scope, Scopes.OpenId)
         && SchemataAuthenticationHandler<TApp, TToken>.IsUserGrant(items)) {
            response.IdToken = SchemataAuthenticationHandler<TApp, TToken>.CreateIdToken(
                issuer, items, id, config.Value.IdTokenLifetime, response.AccessToken, null);
        }

        if (items.TryGetValue(Properties.IssuedTokenType, out var issuedType)
         && !string.IsNullOrWhiteSpace(issuedType)) {
            response.IssuedTokenType = issuedType;
        }

        return response;
    }

    private async Task<AuthorizationCallbackResponse> IssueCallbackAsync(
        string?                      client,
        string?                      scope,
        string?                      subject,
        string?                      app,
        string?                      authorizationName,
        string?                      sid,
        IDictionary<string, string?> items,
        List<Claim>                  access,
        List<Claim>                  id,
        CancellationToken            ct
    ) {
        items.TryGetValue(Properties.ResponseType, out var responseType);
        var responseTypes = responseType!.Split(' ');
        items.TryGetValue(Properties.RedirectUri, out var redirectUri);
        items.TryGetValue(Properties.ResponseMode, out var responseMode);
        var parameters = new Dictionary<string, string?>();
        items.TryGetValue(Properties.State, out var state);
        if (!string.IsNullOrWhiteSpace(state)) parameters[Parameters.State] = state;
        if (!string.IsNullOrWhiteSpace(config.Value.Issuer)) parameters[Claims.Issuer] = config.Value.Issuer;

        string? at = null;
        if (responseTypes.Contains(ResponseTypes.Token)) {
            at = await SchemataAuthenticationHandler<TApp, TToken>.CreateTokenAsync(
                tokens, issuer, access,
                config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken,
                subject, app, authorizationName, sid, _time, ct);
            parameters[Parameters.AccessToken] = at;
            parameters[Parameters.TokenType]   = Schemes.Bearer;
            parameters[Parameters.ExpiresIn]   = ((int)config.Value.AccessTokenLifetime.TotalSeconds).ToString();
        }

        if (responseTypes.Contains(ResponseTypes.Code)) {
            parameters[Parameters.Code] = await CreateAuthorizationCodeAsync(
                client, scope, responseType, subject, app, items, ct);
        }

        if (responseTypes.Contains(ResponseTypes.IdToken)
         && ScopeParser.Contains(scope, Scopes.OpenId)
         && SchemataAuthenticationHandler<TApp, TToken>.IsUserGrant(items)) {
            parameters[Parameters.IdToken] = SchemataAuthenticationHandler<TApp, TToken>.CreateIdToken(
                issuer, items, id, config.Value.IdTokenLifetime, at, parameters.GetValueOrDefault(Parameters.Code));
        }

        return new(redirectUri!, parameters, responseMode);
    }

    private async Task<string> CreateAuthorizationCodeAsync(
        string?                      client,
        string?                      scope,
        string?                      responseType,
        string?                      subject,
        string?                      app,
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
            ResponseType        = responseType,
            CodeChallenge       = challenge,
            CodeChallengeMethod = method,
            MaxAge              = maxAge,
            AuthTime            = authTime,
        };
        var reference = issuer.CreateReference();
        var now       = _time.GetUtcNow().UtcDateTime;
        var entity = new TToken {
            Name              = Identifiers.NewUid().ToString("n"),
            Type              = TokenTypes.AuthorizationCode,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = JsonSerializer.Serialize(payload, json.Value),
            Subject           = subject,
            ExpireTime        = now + config.Value.AuthorizationCodeLifetime,
            Application       = app,
            Authorization     = authorizationName,
            SessionId         = sid,
        };
        await tokens.CreateAsync(entity, ct);
        return reference;
    }
}
