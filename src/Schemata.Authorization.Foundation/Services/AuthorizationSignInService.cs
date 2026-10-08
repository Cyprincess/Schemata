using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
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
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Default transport-neutral sign-in issuer.</summary>
/// <typeparam name="TApp">Application entity type.</typeparam>
public sealed class AuthorizationSignInService<TApp>(
    IOptions<SchemataAuthorizationOptions> config,
    IOptions<JsonSerializerOptions>        json,
    TokenService                           issuer,
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>             tokens,
    IServiceProvider                       services,
    TimeProvider?                          time = null,
    IOpSessionService?                     sessions = null
) : IAuthorizationSignInService
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly IApplicationManager<TApp> _apps = apps;
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

        var binding = items.TryGetValue(Properties.DpopJkt, out var dpopJkt) && !string.IsNullOrWhiteSpace(dpopJkt)
            ? new DpopBinding(dpopJkt) : null;
        var claimsRequest = ClaimsRequest.Parse(items.GetValueOrDefault(Properties.ClaimsRequest))
                         ?? ClaimsRequest.FromUserinfoRequest(items.GetValueOrDefault(Properties.UserinfoClaims));

        var carriedGrant = AuthorizationGrantContexts.Deserialize(items.GetValueOrDefault(Properties.GrantContext));


        if (principal.Identity is not ClaimsIdentity sourceIdentity) {
            throw new InvalidOperationException(
                "Authorization sign-in service requires a principal with a ClaimsIdentity.");
        }
        principal = new ClaimsPrincipal(principal.Identities.Select(value => value.Clone()));
        var identity = (ClaimsIdentity)principal.Identity!;

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
            var claim = new Claim(Claims.AuthorizationDetails, authorizationDetails, JsonClaimValueTypes.Json) {
                Properties = { [ClaimDestinations.AccessToken] = Parameters.Token },
            };
            identity.AddClaim(claim);
        }

        var client = principal.FindFirstValue(Claims.ClientId);
        var application = !string.IsNullOrWhiteSpace(client)
            ? await _apps.FindByClientIdAsync(client, ct)
            : null;
        if (!string.IsNullOrWhiteSpace(client) && application is null) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_CREDENTIALS);
        }
        var app = application?.CanonicalName;

        var subject = principal.FindFirstValue(IdentityClaims.Subject);

        if (callback && sessions is not null) {
            sid = await sessions.IssueAsync(principal, subject, ct) ?? sid;
            if (!string.IsNullOrWhiteSpace(sid)) {
                items[Properties.SessionId] = sid;
                if (identity.FindFirst(Claims.SessionId) is { } currentSid
                    && !string.Equals(currentSid.Value, sid, StringComparison.Ordinal)) {
                    identity.RemoveClaim(currentSid);
                }

                if (!identity.HasClaim(claim => claim.Type == Claims.SessionId)) {
                    identity.AddClaim(new(Claims.SessionId, sid));
                }
            }
        }

        // Resolve the trusted authentication event after session/continuation identity is final,
        // then derive one claims snapshot that every advisor, property, and token row consumes.
        var inherited = carriedGrant;
        var claims = identity.Claims.ToList();
        var source = items.GetValueOrDefault(Properties.GrantType);
        var authentication = inherited?.Authentication;
        if (inherited is null) {
            var provider = services.GetService(typeof(IAuthenticationContextProvider)) as IAuthenticationContextProvider;
            authentication = provider is null
                ? AuthenticationContextExtensions.Read(claims)
                : await provider.GetContextAsync(new(new ClaimsIdentity(claims)), ct);
        }

        var grantScope = string.IsNullOrWhiteSpace(scope) ? inherited?.Scope : scope;
        var grant = inherited is null
            ? AuthorizationGrantContexts.Create(subject, grantScope, sid, source, authentication,
                items.GetValueOrDefault(Properties.GrantProfile))
            : AuthorizationGrantContexts.Narrow(inherited, grantScope, sid);
        var issuance = new AuthorizationClaimContext {
            Grant = grant, RequestedClaims = claimsRequest, Dpop = binding,
            AccessResources = items.GetValueOrDefault(Properties.AccessResources)?.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        };
        // Every claims advisor observes the same resolved event; the authentication-context
        // advisor later owns destination tagging, not event selection.
        AuthenticationContextExtensions.Apply(claims, grant.Authentication, destinations: false);


        switch (await Advisor.For<IClaimsAdvisor>().RunAsync(ctx, claims, issuance, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when !callback && issuance.TokenResponse is { } handled:
                return new(handled, null);
            case AdviseResult.Handle:
                break;
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.AccessDenied, SchemataResources.ACCESS_DENIED);
        }

        foreach (var claim in claims) {
            var destinations = new HashSet<string>();
            switch (await Advisor.For<IDestinationAdvisor>()
                                 .RunAsync(ctx, claim, destinations, principal, issuance, ct)) {
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
        if (callback && grant.NativeSessionKind == NativeSessionKinds.Online && sessions is not null) {
            // Establishment reuses the live slot generation or atomically replaces an expired or
            // invalidated one; the grant persists the generation it was born under, and
            // continuations must prove that exact generation.
            var authority = await sessions.EstablishOnlineAsync(subject, sid, ct);
            if (string.IsNullOrWhiteSpace(authority)) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
            }
            grant.OnlineSessionAuthority = authority;
        }

        AuthorizationSignInResponse response = callback
            ? new(null, await IssueCallbackAsync(
                client, application, scope, subject, app, authorizationName, sid, grant, items, access, id, ctx, ct))
            : new(await IssueTokenAsync(
                application, subject, app, authorizationName, sid, scope, grant, items, access, id, binding, ctx, ct), null);

        // The OIDC artifact is published: record the relying party's participation in this
        // session once, so logout notification survives credential expiry and revocation.
        // Registration is idempotent and OAuth grants never participate.
        if (grant.Profile == GrantProfiles.OpenIdConnect
            && !string.IsNullOrWhiteSpace(subject)
            && !string.IsNullOrWhiteSpace(sid)
            && !string.IsNullOrWhiteSpace(app)) {
            await tokens.RegisterParticipantAsync(subject, sid, app, ct,
                (transaction, cancellation) => _apps.EnlistPublicationAsync(transaction, app, cancellation));
        }

        return response;
    }

    private async Task<TokenResponse> IssueTokenAsync(
        TApp?                        application,
        string?                      subject,
        string?                      app,
        string?                      authorizationName,
        string?                      sid,
        string?                      scope,
        AuthorizationGrantContext    grant,
        IDictionary<string, string?> items,
        List<Claim>                  access,
        List<Claim>                  id,
        DpopBinding?                 binding,
        AdviceContext                ctx,
        CancellationToken            ct
    ) {
        if (grant.NativeSessionKind == NativeSessionKinds.Online
            && (sessions is null
                || !await sessions.ValidateOnlineAsync(subject, sid, grant.OnlineSessionAuthority, ct))) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var issuedAt = _time.GetUtcNow();
        if (grant.ExpiresAt is { } elapsed && elapsed <= issuedAt) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // OIDC Registration §2: the client's registered id_token_signed_response_alg governs the
        // ID token's signature. It applies only when this operation actually issues an ID token;
        // OAuth-only grants and non-user grants sign everything with the primary selection.
        var issuesIdToken = grant.Profile == GrantProfiles.OpenIdConnect
                         && SchemataAuthenticationHandler<TApp>.IsUserGrant(items);
        await using var signing = await issuer.BeginSigningAsync(
            issuesIdToken ? application?.IdTokenSignedResponseAlg ?? SigningAlgorithms.RsaSha256 : null, ct);
        var successors = new List<SchemataToken>();
        var accessExpiresAt = Min(issuedAt + config.Value.AccessTokenLifetime, grant.ExpiresAt);
        var accessToken = SchemataAuthenticationHandler<TApp>.PrepareToken(
            issuer, signing, access, config.Value.AccessTokenFormat, issuedAt, accessExpiresAt,
            TokenTypes.AccessToken, subject, app, authorizationName, sid, grant);
        successors.Add(accessToken);
        var response = new TokenResponse {
            AccessToken = accessToken.ReferenceId!,
            TokenType   = binding is null ? Schemes.Bearer : Schemes.Dpop,
            ExpiresIn   = (int)(accessExpiresAt - issuedAt).TotalSeconds,
            Scope       = scope,
        };

        // RFC 9396 §7: the token response echoes the actual detail set the access token was
        // issued with, as a JSON array.
        if (items.TryGetValue(Properties.AuthorizationDetails, out var actualDetails)
         && !string.IsNullOrWhiteSpace(actualDetails)) {
            response.AuthorizationDetails = JsonSerializer.Deserialize<JsonElement>(actualDetails);
        }


        var createFamily = !grant.FamilyEstablished && !string.IsNullOrWhiteSpace(grant.Family);
        if (SchemataAuthenticationHandler<TApp>.ShouldIssueRefreshToken(items)) {
            var refreshExpiresAt = Min(issuedAt + config.Value.RefreshTokenLifetime, grant.ExpiresAt);
            if (refreshExpiresAt <= issuedAt) {
                throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
            }

            grant.Family ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            grant.ExpiresAt = refreshExpiresAt;
            createFamily = !grant.FamilyEstablished;
            grant.FamilyEstablished = true;
            accessToken.Family = grant.Family;
            accessToken.GrantContext = AuthorizationGrantContexts.Serialize(grant);
            var refreshToken = SchemataAuthenticationHandler<TApp>.PrepareToken(
                issuer, signing, [..access], config.Value.RefreshTokenFormat, issuedAt, refreshExpiresAt,
                TokenTypes.RefreshToken, subject, app, authorizationName, sid, grant);
            successors.Add(refreshToken);
            response.RefreshToken = refreshToken.ReferenceId;
        }

        if (items.TryGetValue(Properties.DeviceSecretToken, out var serializedSecret)
            && !string.IsNullOrWhiteSpace(serializedSecret)) {
            var preparedSecret = JsonSerializer.Deserialize<SchemataToken>(
                serializedSecret, Common.SchemataJson.Default)
                ?? throw new InvalidOperationException("The prepared device secret could not be deserialized.");
            preparedSecret.Family = grant.Family;
            preparedSecret.GrantContext = AuthorizationGrantContexts.Serialize(grant);
            successors.Add(preparedSecret);
        }

        if (issuesIdToken) {
            var idClaims = WithSession(id, sid);

            if (items.TryGetValue(Properties.DeviceSecret, out var deviceSecret)
             && !string.IsNullOrWhiteSpace(deviceSecret)) {
                response.DeviceSecret = deviceSecret;
                idClaims = [..idClaims];
                idClaims.Add(new(Claims.DsHash, TokenService.ComputeHash(deviceSecret, signing.Signing)));
            }

            var idExpiresAt = Min(issuedAt + config.Value.IdTokenLifetime, grant.ExpiresAt);
            response.IdToken = SchemataAuthenticationHandler<TApp>.CreateIdToken(
                issuer, signing, items, idClaims, issuedAt, idExpiresAt, response.AccessToken, null);
        }

        if (items.TryGetValue(Properties.IssuedTokenType, out var issuedType)
         && !string.IsNullOrWhiteSpace(issuedType)) {
            response.IssuedTokenType = issuedType;
        }

        SchemataToken? predecessor = null;
        if (items.TryGetValue(Properties.RefreshPredecessor, out var serializedPredecessor)
            && !string.IsNullOrWhiteSpace(serializedPredecessor)) {
            predecessor = JsonSerializer.Deserialize<SchemataToken>(
                serializedPredecessor, Common.SchemataJson.Default)
                ?? throw new InvalidOperationException("The refresh predecessor could not be deserialized.");
        }

        if (grant.ExpiresAt is { } finalDeadline && _time.GetUtcNow() >= finalDeadline) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var published = predecessor is not null
            ? await tokens.RotateFamilyAsync(predecessor, successors, ct,
                (transaction, cancellation) => _apps.EnlistTokenPublicationAsync(transaction, successors, cancellation))
            : grant.Family is null
                ? await PublishWithoutFamilyAsync(successors, ct)
                : await tokens.PublishFamilyAsync(grant.Family, successors, createFamily, ct,
                    (transaction, cancellation) => _apps.EnlistTokenPublicationAsync(transaction, successors, cancellation));
        if (!published) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        return response;
    }

    private async Task<bool> PublishWithoutFamilyAsync(IEnumerable<SchemataToken> prepared, CancellationToken ct) {
        foreach (var token in prepared) {
            await tokens.CreateAsync(token, ct,
                (transaction, cancellation) => _apps.EnlistTokenPublicationAsync(transaction, [token], cancellation));
        }
        return true;
    }

    private async Task<AuthorizationCallbackResponse> IssueCallbackAsync(
        string?                      client,
        TApp?                        application,
        string?                      scope,
        string?                      subject,
        string?                      app,
        string?                      authorizationName,
        string?                      sid,
        AuthorizationGrantContext    grant,
        IDictionary<string, string?> items,
        List<Claim>                  access,
        List<Claim>                  id,
        AdviceContext                ctx,
        CancellationToken            ct
    ) {
        if (!items.TryGetValue(Properties.ResponseType, out var responseType)
         || string.IsNullOrWhiteSpace(responseType)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.ResponseType });
        }

        var responseTypes = responseType.Split(' ');
        if (!items.TryGetValue(Properties.RedirectUri, out var redirectUri)
         || string.IsNullOrWhiteSpace(redirectUri)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        }

        items.TryGetValue(Properties.ResponseMode, out var responseMode);
        var parameters = new Dictionary<string, string?>();
        items.TryGetValue(Properties.State, out var state);
        if (!string.IsNullOrWhiteSpace(state)) parameters[Parameters.State] = state;
        if (!string.IsNullOrWhiteSpace(config.Value.Issuer)) parameters[Claims.Issuer] = config.Value.Issuer;

        var issuedAt = _time.GetUtcNow();
        string? at = null;

        // The negotiated algorithm applies only when this response carries an ID token;
        // a token-only callback signs with the primary selection. One shared context still
        // serves every token of a combined response.
        var issuesIdToken = responseTypes.Contains(ResponseTypes.IdToken)
                         && grant.Profile == GrantProfiles.OpenIdConnect
                         && SchemataAuthenticationHandler<TApp>.IsUserGrant(items);
        var idTokenAlgorithm = issuesIdToken ? application?.IdTokenSignedResponseAlg : null;

        // A code-only callback mints no JWT; the signing selection resolves on the first
        // token-bearing branch and serves every token the response carries.
        SigningContext? signing = null;
        try {
            if (responseTypes.Contains(ResponseTypes.Token)) {
                var expiresAt = issuedAt + config.Value.AccessTokenLifetime;
                signing ??= await issuer.BeginSigningAsync(idTokenAlgorithm, ct);
                at = await SchemataAuthenticationHandler<TApp>.CreateTokenAsync(
                    tokens, _apps, issuer, signing, access,
                    config.Value.AccessTokenFormat, issuedAt, expiresAt, TokenTypes.AccessToken,
                    subject, app, authorizationName, sid, grant, ct);
                parameters[Parameters.AccessToken] = at;
                parameters[Parameters.TokenType]   = Schemes.Bearer;
                parameters[Parameters.ExpiresIn]   = ((int)(expiresAt - issuedAt).TotalSeconds).ToString();
            }

            if (responseTypes.Contains(ResponseTypes.Code)) {
                parameters[Parameters.Code] = await CreateAuthorizationCodeAsync(
                    client, scope, responseType, subject, app, grant, items, ct);
            }

            if (issuesIdToken) {
                var idExpiresAt = Min(issuedAt + config.Value.IdTokenLifetime, grant.ExpiresAt);
                signing ??= await issuer.BeginSigningAsync(idTokenAlgorithm, ct);
                parameters[Parameters.IdToken] = SchemataAuthenticationHandler<TApp>.CreateIdToken(
                    issuer, signing, items, WithSession(id, sid), issuedAt, idExpiresAt, at,
                    parameters.GetValueOrDefault(Parameters.Code));
            }

            if (grant.Profile == GrantProfiles.OpenIdConnect
                && items.TryGetValue(Properties.SessionStateSalt, out var salt)
                && !string.IsNullOrWhiteSpace(salt)
                && !string.IsNullOrWhiteSpace(client)
                && SessionStateFormulator.OriginOf(redirectUri) is { } origin
                && services.GetService(typeof(Microsoft.AspNetCore.Http.IHttpContextAccessor))
                       is Microsoft.AspNetCore.Http.IHttpContextAccessor { HttpContext: { } http }
                && services.GetService(typeof(SessionStateFormulator)) is SessionStateFormulator formulator
                && services.GetService(typeof(IOptions<SessionManagementOptions>))
                       is IOptions<SessionManagementOptions> sessionManagement) {
                // Recompute from the final browser/session state: session issuance above may have
                // persisted a rotated opstate, and only the random salt crossed the interaction.
                var opUa = OpState.GetOrCreate(http, sessionManagement.Value.OpStateCookieName);
                var sessionState = formulator.Build(client, origin, opUa, salt);
                SessionStateContext.Set(http, sessionState);
                parameters[Parameters.SessionState] = sessionState;
            }

            return new(redirectUri, parameters, ResponseModeService.ResolveMode(responseMode, responseType));
        } finally {
            if (signing is not null) {
                await signing.DisposeAsync();
            }
        }
    }

    private static DateTimeOffset Min(DateTimeOffset policy, DateTimeOffset? deadline) {
        return deadline is { } value && value < policy ? value : policy;
    }

    private static List<Claim> WithSession(List<Claim> claims, string? sid) {
        return string.IsNullOrWhiteSpace(sid)
               || claims.Any(claim => claim.Type == Claims.SessionId)
            ? claims
            : [..claims, new(Claims.SessionId, sid)];
    }

    private async Task<string> CreateAuthorizationCodeAsync(
        string?                      client,
        string?                      scope,
        string?                      responseType,
        string?                      subject,
        string?                      app,
        AuthorizationGrantContext    grant,
        IDictionary<string, string?> items,
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
        items.TryGetValue(Properties.ClaimsRequest, out var claimsRequest);
        request.Claims = claimsRequest;

        // Persist the resolved original event with the code; code exchange inherits it directly.
        var payload = new AuthorizationCodePayload {
            Request = request,
            Grant   = grant,
        };

        var reference = issuer.CreateReference();
        var now       = _time.GetUtcNow().UtcDateTime;
        var entity = new SchemataToken {
            Type              = TokenTypes.AuthorizationCode,
            Status            = TokenStatuses.Valid,
            ReferenceId       = reference,
            Payload           = JsonSerializer.Serialize(payload, json.Value),
            Parent            = subject,
            ExpireTime        = now + config.Value.AuthorizationCodeLifetime,
            Application       = app,
            Authorization     = authorizationName,
            SessionId         = sid,
            Family             = grant.Family,
            GrantContext       = AuthorizationGrantContexts.Serialize(grant),
        };
        await tokens.CreateAsync(entity, ct,
            (transaction, cancellation) => _apps.EnlistTokenPublicationAsync(transaction, [entity], cancellation));
        return reference;
    }
}
