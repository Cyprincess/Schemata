using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Abstractions;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Default transport-neutral sign-in issuer.</summary>
/// <typeparam name="TApp">Application entity type.</typeparam>
public sealed class AuthorizationSignInService<TApp>(
    IOptions<SchemataAuthorizationOptions> config,
    IOptions<JsonSerializerOptions>        json,
    TokenService                           issuer,
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>                    tokens,
    IServiceProvider                       services,
    TimeProvider?                          time = null
) : IAuthorizationSignInService
    where TApp : SchemataApplication
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
        using var ambient = ctx is null ? AdviceContext.Establish(ctx = new(services)) : null;

        // The token endpoint dispatch ferries a DPoP key binding through the result
        // properties; publish it on the ambient context for the claim assembly below.
        if (items.TryGetValue(Properties.DpopJkt, out var dpopJkt) && !string.IsNullOrWhiteSpace(dpopJkt)) {
            ctx.Set(new DpopBinding(dpopJkt));
        }

        if (principal.Identity is not ClaimsIdentity identity) {
            throw new InvalidOperationException(
                "Authorization sign-in service requires a principal with a ClaimsIdentity.");
        }

        items.TryGetValue(Properties.Scope, out var scope);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);
        if (!string.IsNullOrWhiteSpace(scope)) identity.AddClaim(new(Claims.Scope, scope));
        items.TryGetValue(Properties.Resources, out var resources);
        if (!string.IsNullOrWhiteSpace(resources)) identity.AddClaim(new(Claims.Resources, resources));
        if (!string.IsNullOrWhiteSpace(sid)) identity.AddClaim(new(Claims.SessionId, sid));

        // RFC 9396 §9.1: granted authorization details ride the access token as a top-level
        // JSON-array claim, tagged for the access token destination only.
        items.TryGetValue(Properties.AuthorizationDetails, out var authorizationDetails);
        if (!string.IsNullOrWhiteSpace(authorizationDetails)) {
            var claim = new Claim(Claims.AuthorizationDetails, authorizationDetails, JsonClaimValueTypes.Json);
            claim.Properties[ClaimDestinations.AccessToken] = Parameters.Token;
            identity.AddClaim(claim);
        }

        var claims = identity.Claims.ToList();
        var client = principal.FindFirstValue(Claims.ClientId);
        var app = !string.IsNullOrWhiteSpace(client)
            ? (await apps.FindByClientIdAsync(client, ct))?.CanonicalName
            : null;
        var subject = principal.FindFirstValue(IdentityClaims.Subject);

        // RFC 9449 §6.1: a DPoP-bound token carries the proof key thumbprint under cnf.jkt;
        // the claim is tagged for the access token destination only.
        var binding = ctx.TryGet<DpopBinding>(out var dpop) ? dpop : null;
        if (binding is not null) {
            var cnf = new Claim(Claims.Cnf, $"{{\"jkt\":\"{binding.Jkt}\"}}", JsonClaimValueTypes.Json);
            cnf.Properties[ClaimDestinations.AccessToken] = Parameters.Token;
            claims.Add(cnf);
        }

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
                client, scope, subject, app, authorizationName, sid, items, access, id, ctx, ct))
            : new(await IssueTokenAsync(
                subject, app, authorizationName, sid, scope, items, access, id, binding, ct), null);
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
        DpopBinding?                 binding,
        CancellationToken            ct
    ) {
        var at = await SchemataAuthenticationHandler<TApp>.CreateTokenAsync(
            tokens, issuer, access,
            config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken,
            subject, app, authorizationName, sid, _time, ct);
        var response = new TokenResponse {
            AccessToken = at,
            TokenType   = binding is null ? Schemes.Bearer : Schemes.Dpop,
            ExpiresIn   = (int)config.Value.AccessTokenLifetime.TotalSeconds,
            Scope       = scope,
        };

        if (SchemataAuthenticationHandler<TApp>.ShouldIssueRefreshToken(items)) {
            response.RefreshToken = await SchemataAuthenticationHandler<TApp>.CreateTokenAsync(
                tokens, issuer, [..access],
                config.Value.RefreshTokenFormat, config.Value.RefreshTokenLifetime, TokenTypes.RefreshToken,
                subject, app, authorizationName, sid, _time, ct);
        }

        if (ScopeParser.Contains(scope, Scopes.OpenId)
         && SchemataAuthenticationHandler<TApp>.IsUserGrant(items)) {
            response.IdToken = await SchemataAuthenticationHandler<TApp>.CreateIdToken(
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
        AdviceContext                ctx,
        CancellationToken            ct
    ) {
        if (!items.TryGetValue(Properties.ResponseType, out var responseType)
         || string.IsNullOrWhiteSpace(responseType)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ResponseType)
            );
        }

        var responseTypes = responseType.Split(' ');
        if (!items.TryGetValue(Properties.RedirectUri, out var redirectUri)
         || string.IsNullOrWhiteSpace(redirectUri)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST)
            );
        }

        items.TryGetValue(Properties.ResponseMode, out var responseMode);
        var parameters = new Dictionary<string, string?>();
        items.TryGetValue(Properties.State, out var state);
        if (!string.IsNullOrWhiteSpace(state)) parameters[Parameters.State] = state;
        if (!string.IsNullOrWhiteSpace(config.Value.Issuer)) parameters[Claims.Issuer] = config.Value.Issuer;

        string? at = null;
        if (responseTypes.Contains(ResponseTypes.Token)) {
            at = await SchemataAuthenticationHandler<TApp>.CreateTokenAsync(
                tokens, issuer, access,
                config.Value.AccessTokenFormat, config.Value.AccessTokenLifetime, TokenTypes.AccessToken,
                subject, app, authorizationName, sid, _time, ct);
            parameters[Parameters.AccessToken] = at;
            parameters[Parameters.TokenType]   = Schemes.Bearer;
            parameters[Parameters.ExpiresIn]   = ((int)config.Value.AccessTokenLifetime.TotalSeconds).ToString();
        }

        if (responseTypes.Contains(ResponseTypes.Code)) {
            parameters[Parameters.Code] = await CreateAuthorizationCodeAsync(
                client, scope, responseType, subject, app, items, ctx, ct);
        }

        if (responseTypes.Contains(ResponseTypes.IdToken)
         && ScopeParser.Contains(scope, Scopes.OpenId)
         && SchemataAuthenticationHandler<TApp>.IsUserGrant(items)) {
            parameters[Parameters.IdToken] = await SchemataAuthenticationHandler<TApp>.CreateIdToken(
                issuer, items, id, config.Value.IdTokenLifetime, at, parameters.GetValueOrDefault(Parameters.Code));
        }

        return new(redirectUri, parameters, ResponseModeService.ResolveMode(responseMode, responseType));
    }

    private async Task<string> CreateAuthorizationCodeAsync(
        string?                      client,
        string?                      scope,
        string?                      responseType,
        string?                      subject,
        string?                      app,
        IDictionary<string, string?> items,
        AdviceContext                ctx,
        CancellationToken            ct
    ) {
        items.TryGetValue(Properties.RedirectUri, out var redirect);
        items.TryGetValue(Properties.Nonce, out var nonce);
        items.TryGetValue(Properties.CodeChallenge, out var challenge);
        items.TryGetValue(Properties.CodeChallengeMethod, out var method);
        items.TryGetValue(Properties.MaxAge, out var maxAge);
        items.TryGetValue(Properties.DpopJkt, out var dpopJkt);
        items.TryGetValue(Properties.AuthorizationName, out var authorizationName);
        items.TryGetValue(Properties.SessionId, out var sid);
        items.TryGetValue(Properties.Resources, out var resources);
        var request = new AuthorizeRequest {
            ClientId            = client,
            RedirectUri         = redirect,
            Scope               = scope,
            Nonce               = nonce,
            ResponseType        = responseType,
            CodeChallenge       = challenge,
            CodeChallengeMethod = method,
            DpopJkt             = dpopJkt,
            MaxAge              = maxAge,
        };
        if (!string.IsNullOrWhiteSpace(resources)) {
            request.Resource = resources.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        items.TryGetValue(Properties.AuthorizationDetails, out var authorizationDetails);
        request.AuthorizationDetails = authorizationDetails;

        // The claims advisor publishes the approved authentication context; persisting it lets
        // the later code exchange mint acr/amr/auth_time without a session.
        var payload = new AuthorizationCodePayload { Request = request };
        if (ctx.TryGet<AuthenticationContext>(out var context)) {
            payload.Context = context;
        }

        var reference = issuer.CreateReference();
        var now       = _time.GetUtcNow().UtcDateTime;
        var entity = new SchemataToken {
            Name              = Guid.NewGuid().ToString("n"),
            Type              = TokenTypes.AuthorizationCode,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = JsonSerializer.Serialize(payload, json.Value),
            Parent            = subject,
            ExpireTime        = now + config.Value.AuthorizationCodeLifetime,
            Application       = app,
            Authorization     = authorizationName,
            SessionId         = sid,
        };
        await tokens.CreateAsync(entity, ct);
        return reference;
    }
}
