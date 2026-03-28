using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Extensions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class AuthorizeInteractionHandler<TApp, TAuth, TScope, TToken> : IInteractionHandler
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken
{
    private readonly IApplicationManager<TApp>              _apps;
    private readonly IAuthorizationManager<TAuth>           _auths;
    private readonly IScopeManager<TScope>                  _scopes;
    private readonly ITokenManager<TToken>                  _tokens;
    private readonly IOptions<SchemataAuthorizationOptions> _options;
    private readonly IOptions<JsonSerializerOptions>        _json;

    public AuthorizeInteractionHandler(
        IApplicationManager<TApp>              apps,
        IAuthorizationManager<TAuth>           auths,
        IScopeManager<TScope>                  scopes,
        ITokenManager<TToken>                  tokens,
        IOptions<JsonSerializerOptions>        json,
        IOptions<SchemataAuthorizationOptions> options
    ) {
        _apps    = apps;
        _auths   = auths;
        _scopes  = scopes;
        _tokens  = tokens;
        _json    = json;
        _options = options;
    }

    #region IInteractionHandler Members

    /// <inheritdoc />
    public string CodeType => TokenTypeUris.Interaction;

    public async Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    ) {
        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction?.Status != TokenStatuses.Valid
         || interaction.Type != TokenTypes.Interaction
         || (interaction.ExpireTime.HasValue && interaction.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(interaction.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(interaction.Payload, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var application = await _apps.FindByCanonicalNameAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var requested = ScopeParser.Parse(authorize.Scope);

        var scopes = await _scopes.ResolveScopesAsync(requested, ct)
                                  .Map(s => new ScopeResponse {
                                       Name         = s.Name,
                                       DisplayName  = s.DisplayName,
                                       DisplayNames = s.DisplayNames,
                                       Description  = s.Description,
                                       Descriptions = s.Descriptions,
                                   }, ct).ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(authorize.ResponseType)) {
            authorize.ResponseMode = ResponseModeService.ResolveMode(authorize.ResponseMode, authorize.ResponseType);
        }

        return AuthorizationResult.Content(new InteractionResponse {
            Type    = InteractionTypes.Authorize,
            Request = authorize,
            Application = new() {
                ClientId     = application.ClientId,
                DisplayName  = application.DisplayName,
                DisplayNames = application.DisplayNames,
            },
            Scopes = scopes,
        });
    }

    public async Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    ) {
        var subject = principal.FindFirstValue(Claims.Subject);
        if (string.IsNullOrWhiteSpace(subject)) {
            return AuthorizationResult.Challenge();
        }

        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction?.Status != TokenStatuses.Valid
         || interaction.Type != TokenTypes.Interaction
         || (interaction.ExpireTime.HasValue && interaction.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(interaction.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(interaction.Payload, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var application = await _apps.FindByCanonicalNameAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var claims = new List<Claim> {
            new(Claims.Subject, subject),
            new(Claims.ClientId, application.ClientId),
        };

        var response = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        var mode     = ResponseModeService.ResolveMode(authorize.ResponseMode, authorize.ResponseType);

        var sid = principal.FindFirstValue(_options.Value.SessionIdClaimType);
        var at  = principal.FindFirstValue(Claims.AuthTime);

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = authorize.Scope,
            [Properties.ResponseType]        = authorize.ResponseType,
            [Properties.Nonce]               = authorize.Nonce,
            [Properties.RedirectUri]         = authorize.RedirectUri,
            [Properties.ResponseMode]        = mode,
            [Properties.State]               = authorize.State,
            [Properties.CodeChallenge]       = authorize.CodeChallenge,
            [Properties.CodeChallengeMethod] = authorize.CodeChallengeMethod,
            [Properties.SessionId]           = sid,
            [Properties.MaxAge]              = authorize.MaxAge,
            [Properties.AuthTime]            = at,
        };

        await _tokens.RevokeAsync(interaction, ct);

        var authorization = new TAuth {
            ApplicationName = application.Name,
            Subject         = subject,
            Type            = AuthorizationTypes.AdHoc,
            Status          = TokenStatuses.Valid,
            Scopes          = authorize.Scope,
        };

        await _auths.CreateAsync(authorization, ct);

        properties[Properties.AuthorizationName] = authorization.Name;

        return AuthorizationResult.SignIn(response, properties);
    }

    public async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction is null) {
            return;
        }

        await _tokens.RevokeAsync(interaction, ct);
    }

    #endregion
}
