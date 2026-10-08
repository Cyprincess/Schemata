using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class NativeSsoTokenExchangeHandler<TApp, TAuth> : ITokenExchangeHandler<TApp>
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization
{
    private readonly ITokenStore<SchemataToken> _tokens;
    private readonly TokenService _issuer;
    private readonly IApplicationManager<TApp> _apps;
    private readonly SchemataAuthorizationOptions _options;
    private readonly ISubjectIdentifierService _subjects;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly IOpSessionService? _sessions;
    private readonly IAuthorizationManager<TAuth> _authorizations;
    private readonly IConsentModelProvider _consent;

    public NativeSsoTokenExchangeHandler(
        ITokenStore<SchemataToken>             tokens,
        TokenService                           issuer,
        IApplicationManager<TApp>              apps,
        IOptions<SchemataAuthorizationOptions> options,
        ISubjectIdentifierService              subjects,
        IServiceProvider                       services,
        IAuthorizationManager<TAuth>           authorizations,
        IConsentModelProvider                  consent,
        TimeProvider?                          time = null,
        IOpSessionService?                     sessions = null
    ) {
        _tokens  = tokens;
        _issuer  = issuer;
        _apps    = apps;
        _options = options.Value;
        _subjects = subjects;
        _services = services;
        _sessions = sessions;
        _time    = time ?? TimeProvider.System;
        _authorizations = authorizations;
        _consent = consent;
    }

    public string SubjectTokenType => TokenTypeUris.IdToken;

    public async Task<AuthorizationResult> HandleAsync(
        TApp              application,
        TokenRequest      request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        // §4.1: profile inputs are exact.
        if (!string.Equals(request.SubjectTokenType, TokenTypeUris.IdToken, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.SubjectTokenType });
        }

        if (!string.Equals(request.ActorTokenType, TokenTypeUris.DeviceSecret, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.ActorTokenType });
        }

        var requested = string.IsNullOrWhiteSpace(request.RequestedTokenType)
            ? TokenTypeUris.AccessToken
            : request.RequestedTokenType;
        if (!string.Equals(requested, TokenTypeUris.AccessToken, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.RequestedTokenType });
        }

        if (request.Audience is not { Count: 1 }
         || !string.Equals(request.Audience.First(), _options.Issuer, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        if (!string.IsNullOrWhiteSpace(request.Scope) && !ScopeParser.Contains(request.Scope, Scopes.OpenId)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.Scope });
        }

        if (string.IsNullOrWhiteSpace(request.ActorToken) || string.IsNullOrWhiteSpace(request.SubjectToken)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_EMPTY, new Dictionary<string, string?> { ["value"] = Parameters.ActorToken });
        }

        // §4.3 step 1: device_secret valid.
        var now = _time.GetUtcNow().UtcDateTime;
        var deviceToken = await _tokens.FindByReferenceIdAsync(request.ActorToken, ct);
        if (deviceToken is null
         || deviceToken.Type != TokenTypes.DeviceSecret
         || deviceToken.Status != TokenStatuses.Valid
         || (deviceToken.ExpireTime is { } expires && expires <= now)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }
        var sourceContext = AuthorizationGrantContexts.Deserialize(deviceToken.GrantContext);
        if (sourceContext is null || sourceContext.Profile != GrantProfiles.OpenIdConnect
            || sourceContext.SubjectKind != GrantSubjectKinds.EndUser
            || string.IsNullOrWhiteSpace(application.CanonicalName)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // §4.3 step 2: id_token signature verified; lifetime intentionally NOT checked.
        var idPrincipal = await _issuer.Validate(request.SubjectToken, audience: null, lifetime: false);
        if (idPrincipal is null) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var aud = idPrincipal.FindFirstValue(Claims.Audience);
        var source = string.IsNullOrWhiteSpace(aud) ? null : await _apps.FindByClientIdAsync(aud, ct);
        if (source is null || string.IsNullOrWhiteSpace(source.CanonicalName)
            || !string.Equals(deviceToken.Application, source.CanonicalName, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // §4.3 step 3: ds_hash matches the algorithm of the presented ID token.
        var algorithm = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(request.SubjectToken).Alg;
        var expected  = TokenService.ComputeHash(request.ActorToken, algorithm);
        var presented = idPrincipal.FindFirstValue(Claims.DsHash);
        if (!string.Equals(expected, presented, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // §4.3 step 4: the ID token sid must match the device-secret session and remain active.
        var sid = idPrincipal.FindFirstValue(Claims.SessionId);
        if (string.IsNullOrWhiteSpace(sid)
            || !string.Equals(sid, deviceToken.SessionId, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var subject = sourceContext.Subject;
        if (string.IsNullOrWhiteSpace(subject)
            || !string.Equals(_subjects.Resolve(subject, source),
                idPrincipal.FindFirstValue(IdentityClaims.Subject), StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var sessionActive = sourceContext.NativeSessionKind switch {
            NativeSessionKinds.Online => _sessions is not null
                && await _sessions.ValidateOnlineAsync(subject, sid, sourceContext.OnlineSessionAuthority, ct),
            NativeSessionKinds.Offline => sourceContext.FamilyEstablished
                && string.Equals(deviceToken.Family, sourceContext.Family, StringComparison.Ordinal)
                && await _tokens.IsFamilyActiveAsync(sourceContext.Family!, ct),
            var _ => false,
        };
        if (!sessionActive) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var provider = _services.GetService<ISubjectProvider>();
        if (provider is not null && !await provider.ValidateAsync(subject, ct)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        // §4.3 step 5: both source and target clients registered the device_sso scope.
        if (!await _apps.HasScopeAsync(source, Scopes.DeviceSso, ct)
         || !await _apps.HasScopeAsync(application, Scopes.DeviceSso, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient, SchemataResources.UNAUTHORIZED_GRANT_TYPE);
        }

        var requestedScope = string.IsNullOrWhiteSpace(request.Scope) ? sourceContext.Scope : request.Scope;
        var requestedScopes = ScopeParser.Parse(requestedScope);
        if (!requestedScopes.IsSubsetOf(ScopeParser.Parse(application.Scope))) {
            throw new OAuthException(OAuthErrors.InvalidScope, SchemataResources.INVALID_SCOPE);
        }
        var approved = _consent.Resolve(application, new() { Scope = requestedScope }) == ConsentModel.Implicit;
        if (!approved) {
            await foreach (var authorization in _authorizations.ListAsync(subject, application.CanonicalName, ct)) {
                if (authorization.Status == TokenStatuses.Valid
                    && authorization.Type is AuthorizationTypes.AdHoc or AuthorizationTypes.Permanent
                    && requestedScopes.IsSubsetOf(ScopeParser.Parse(authorization.Scopes))) {
                    approved = true;
                    break;
                }
            }
        }
        if (!approved) throw new OAuthException(OAuthErrors.InteractionRequired, SchemataResources.USER_CONSENT_REQUIRED);

        if (!string.Equals(sourceContext.Subject, subject, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }
        if (string.IsNullOrWhiteSpace(deviceToken.Family)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }
        sourceContext.Family = deviceToken.Family;
        sourceContext.FamilyEstablished = true;

        var grant = AuthorizationGrantContexts.Narrow(
            sourceContext,
            requestedScope,
            sid);

        var identity = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, subject),
            new(Claims.ClientId, application.ClientId!),
        ], SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType]      = GrantTypes.TokenExchange,
            [Properties.Scope]          = requestedScope,
            [Properties.Resources]      = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
            [Properties.SessionId]      = sid,
            [Properties.GrantContext]   = AuthorizationGrantContexts.Serialize(grant),
            [Properties.IssuedTokenType] = TokenTypeUris.AccessToken,
        });
    }

}