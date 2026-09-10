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

public sealed class NativeSsoTokenExchangeHandler<TApp> : ITokenExchangeHandler<TApp>
    where TApp : SchemataApplication
{
    private readonly ITokenStore<SchemataToken> _tokens;
    private readonly TokenService _issuer;
    private readonly IApplicationManager<TApp> _apps;
    private readonly SchemataAuthorizationOptions _options;
    private readonly ISubjectIdentifierService _subjects;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;

    public NativeSsoTokenExchangeHandler(
        ITokenStore<SchemataToken>             tokens,
        TokenService                           issuer,
        IApplicationManager<TApp>              apps,
        IOptions<SchemataAuthorizationOptions> options,
        ISubjectIdentifierService              subjects,
        IServiceProvider                       services,
        TimeProvider?                          time = null
    ) {
        _tokens  = tokens;
        _issuer  = issuer;
        _apps    = apps;
        _options = options.Value;
        _subjects = subjects;
        _services = services;
        _time    = time ?? TimeProvider.System;
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
            throw new OAuthException(OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.SubjectTokenType));
        }

        if (!string.Equals(request.ActorTokenType, TokenTypeUris.DeviceSecret, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.ActorTokenType));
        }

        var requested = string.IsNullOrWhiteSpace(request.RequestedTokenType)
            ? TokenTypeUris.AccessToken
            : request.RequestedTokenType;
        if (!string.Equals(requested, TokenTypeUris.AccessToken, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.RequestedTokenType));
        }

        if (!string.Equals(request.Audience, _options.Issuer, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        if (!string.IsNullOrWhiteSpace(request.Scope) && !ScopeParser.Contains(request.Scope, Scopes.OpenId)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_SUPPORTED), Parameters.Scope));
        }

        if (string.IsNullOrWhiteSpace(request.ActorToken) || string.IsNullOrWhiteSpace(request.SubjectToken)) {
            throw new OAuthException(OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ActorToken));
        }

        // §4.3 step 1: device_secret valid.
        var now = _time.GetUtcNow().UtcDateTime;
        var deviceToken = await _tokens.FindByReferenceIdAsync(request.ActorToken, ct);
        if (deviceToken is null
         || deviceToken.Type != TokenTypes.DeviceSecret
         || deviceToken.Status != TokenStatuses.Valid
         || (deviceToken.ExpireTime is { } expires && expires <= now)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        // §4.3 step 2: id_token signature verified; lifetime intentionally NOT checked.
        var idPrincipal = await _issuer.Validate(request.SubjectToken, audience: null, lifetime: false);
        if (idPrincipal is null) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        var aud = idPrincipal.FindFirstValue(Claims.Audience);
        var source = string.IsNullOrWhiteSpace(aud) ? null : await _apps.FindByClientIdAsync(aud, ct);
        if (source is null || string.IsNullOrWhiteSpace(source.CanonicalName)
            || !string.Equals(deviceToken.Application, source.CanonicalName, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        // §4.3 step 3: ds_hash matches the algorithm of the presented ID token.
        var algorithm = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(request.SubjectToken).Alg;
        var expected  = TokenService.ComputeHash(request.ActorToken, algorithm);
        var presented = idPrincipal.FindFirstValue(Claims.DsHash);
        if (!string.Equals(expected, presented, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        // §4.3 step 4: the ID token sid must match the device-secret session and remain active.
        var sid = idPrincipal.FindFirstValue(Claims.SessionId);
        if (string.IsNullOrWhiteSpace(sid)
            || !string.Equals(sid, deviceToken.SessionId, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        string? subject = null;
        var sourceApplication = SecurityParents.Application(source);
        await foreach (var token in _tokens.ListBySessionAsync(sid, ct)) {
            if (token.Status != TokenStatuses.Valid
                || token.Type is not (TokenTypes.AccessToken
                                   or TokenTypes.RefreshToken
                                   or TokenTypes.AuthorizationCode
                                   or TokenTypes.IdToken)
                || (token.ExpireTime is { } expiration && expiration <= now)
                || !string.Equals(token.SessionId, sid, StringComparison.Ordinal)
                || !string.Equals(token.Application, sourceApplication, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(token.Parent)) {
                continue;
            }

            if (subject is not null && !string.Equals(subject, token.Parent, StringComparison.Ordinal)) {
                throw new OAuthException(OAuthErrors.InvalidGrant,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
            }

            subject = token.Parent;
        }

        if (subject is null
            || !string.Equals(_subjects.Resolve(subject, source),
                idPrincipal.FindFirstValue(IdentityClaims.Subject), StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        var provider = _services.GetService<ISubjectProvider>();
        if (provider is not null && !await provider.ValidateAsync(subject, ct)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        // §4.3 step 5: both source and target clients carry s:device_sso.
        if (!await _apps.HasPermissionAsync(source, PermissionPrefixes.Scope + Scopes.DeviceSso, ct)
         || !await _apps.HasPermissionAsync(application, PermissionPrefixes.Scope + Scopes.DeviceSso, ct)) {
            throw new OAuthException(OAuthErrors.UnauthorizedClient,
                SchemataResources.GetResourceString(SchemataResources.UNAUTHORIZED_GRANT_TYPE));
        }

        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            var grantedScopes = source.Permissions?
                                       .Where(p => p.StartsWith(AuthorizationConstants.PermissionPrefixes.Scope, StringComparison.Ordinal))
                                       .Select(p => p[AuthorizationConstants.PermissionPrefixes.Scope.Length..])
                                       .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                if (!grantedScopes.Contains(s)) {
                    throw new OAuthException(OAuthErrors.InteractionRequired,
                        SchemataResources.GetResourceString(SchemataResources.ACCESS_DENIED));
                }
            }
        }

        var identity = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(IdentityClaims.Subject, subject),
            new Claim(Claims.ClientId, application.ClientId!),
        ], SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType]      = GrantTypes.TokenExchange,
            [Properties.Scope]          = request.Scope,
            [Properties.Resources]      = request.Resource is { Count: > 0 } ? string.Join(" ", request.Resource) : null,
            [Properties.SessionId]      = sid,
            [Properties.IssuedTokenType] = TokenTypeUris.AccessToken,
        });
    }

}