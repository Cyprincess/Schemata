using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
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
    private readonly TimeProvider _time;

    public NativeSsoTokenExchangeHandler(
        ITokenStore<SchemataToken>             tokens,
        TokenService                           issuer,
        IApplicationManager<TApp>              apps,
        IOptions<SchemataAuthorizationOptions> options,
        TimeProvider?                          time = null
    ) {
        _tokens  = tokens;
        _issuer  = issuer;
        _apps    = apps;
        _options = options.Value;
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
        var deviceToken = await _tokens.FindByReferenceIdAsync(request.ActorToken, ct);
        if (deviceToken is null
         || deviceToken.Type != TokenTypes.DeviceSecret
         || deviceToken.Status != TokenStatuses.Valid
         || (deviceToken.ExpireTime is { } expires && expires <= _time.GetUtcNow().UtcDateTime)) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        var sourceClientId = DeviceSecretBinding.ClientId(deviceToken);
        var source = string.IsNullOrWhiteSpace(sourceClientId)
            ? null
            : await _apps.FindByClientIdAsync(sourceClientId, ct);
        // §4.3 step 2: id_token signature verified; lifetime intentionally NOT checked.
        var idPrincipal = await _issuer.Validate(request.SubjectToken, audience: null, lifetime: false);
        if (idPrincipal is null) {
            throw new OAuthException(OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }

        var aud = idPrincipal.FindFirstValue(Claims.Audience);
        if (source is null || !string.Equals(aud, source.ClientId, StringComparison.Ordinal)) {
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
            || !string.Equals(sid, deviceToken.SessionId, StringComparison.Ordinal)
            || !await HasValidSessionTokenAsync(sid, ct)) {
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

        var accessToken = _issuer.CreateReference();
        var now         = _time.GetUtcNow().UtcDateTime;
        await _tokens.CreateAsync(new SchemataToken {
            Type        = TokenTypes.AccessToken,
            Status      = TokenStatuses.Valid,
            Format      = TokenFormats.Reference,
            ReferenceId = accessToken,
            Application = SecurityParents.Application(application),
            SessionId   = sid,
            Parent      = idPrincipal.FindFirstValue(IdentityClaims.Subject),
            ExpireTime  = now + _options.AccessTokenLifetime,
        }, ct);

        var response = new TokenResponse {
            AccessToken     = accessToken,
            TokenType       = Schemes.Bearer,
            ExpiresIn       = (int)_options.AccessTokenLifetime.TotalSeconds,
            IssuedTokenType = TokenTypeUris.AccessToken,
            Scope           = request.Scope,
        };

        return AuthorizationResult.Content(response);
    }

    private async Task<bool> HasValidSessionTokenAsync(string sid, CancellationToken ct) {
        await foreach (var token in _tokens.ListBySessionAsync(sid, ct)) {
            if (token.Status == TokenStatuses.Valid
                && token.Type is TokenTypes.AccessToken
                              or TokenTypes.RefreshToken
                              or TokenTypes.AuthorizationCode
                              or TokenTypes.IdToken) {
                return true;
            }

        }
        return false;
    }
}