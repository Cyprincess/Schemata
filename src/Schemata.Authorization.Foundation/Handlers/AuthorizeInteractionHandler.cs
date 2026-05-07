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

/// <summary>
///     Handles the consent/login interaction flow for the OAuth 2.0 authorization endpoint.
///     An SPA calls GET to render the consent screen and POST to approve or deny.
///     Implements <see cref="IInteractionHandler" /> for <see cref="TokenTypeUris.Interaction" />.
/// </summary>
public sealed class AuthorizeInteractionHandler<TApp, TAuth, TScope, TToken> : IInteractionHandler
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken
{
    private readonly IApplicationManager<TApp>              _apps;
    private readonly IAuthorizationManager<TAuth>           _auths;
    private readonly IOptions<JsonSerializerOptions>        _json;
    private readonly IOptions<SchemataAuthorizationOptions> _options;
    private readonly IScopeManager<TScope>                  _scopes;
    private readonly ITokenManager<TToken>                  _tokens;

    /// <summary>
    ///     Initializes the handler with the required managers and configuration.
    /// </summary>
    /// <param name="apps">Application registry.</param>
    /// <param name="auths">Authorization storage for consent records.</param>
    /// <param name="scopes">Scope resolver.</param>
    /// <param name="tokens">Token storage.</param>
    /// <param name="json">JSON serialization options.</param>
    /// <param name="options">Server-level authorization configuration.</param>
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

    /// <summary>
    ///     The token type URI this handler processes — always <see cref="TokenTypeUris.Interaction" />.
    /// </summary>
    public string CodeType => TokenTypeUris.Interaction;

    /// <summary>
    ///     Returns details the consent SPA needs to render: the original
    ///     <see cref="AuthorizeRequest" />, resolved scope metadata, and the client application info.
    /// </summary>
    /// <param name="request">Interaction request containing the reference token code.</param>
    /// <param name="issuer">Token issuer URI.</param>
    /// <param name="ct">Cancellation token.</param>
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
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(interaction.Payload, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var application = await _apps.FindByClientIdAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
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

        // Re-resolve response_mode from response_type when it was not explicitly
        // provided, so the SPA can display the correct callback method.
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

    /// <summary>
    ///     Approves the authorization request: revokes the interaction token,
    ///     creates a consent record (<typeparamref name="TAuth" />), and returns
    ///     a <see cref="AuthorizationResult.SignIn" /> carrying all auth properties
    ///     needed by <see cref="SchemataAuthorizationCodeHandler{TApp, TToken}" />.
    /// </summary>
    /// <param name="request">Interaction request containing the reference token code.</param>
    /// <param name="principal">The authenticated resource owner.</param>
    /// <param name="issuer">Token issuer URI.</param>
    /// <param name="ct">Cancellation token.</param>
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
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(interaction.Payload, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var application = await _apps.FindByClientIdAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
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

        // Record consent so future requests for the same client/scope can skip interaction.
        var authorization = new TAuth {
            ApplicationName     = application.Name,
            Subject             = subject,
            Type                = AuthorizationTypes.AdHoc,
            Status              = TokenStatuses.Valid,
            Scopes              = authorize.Scope,
            RedirectUri         = authorize.RedirectUri,
            ResponseType        = authorize.ResponseType,
            CodeChallengeMethod = authorize.CodeChallengeMethod,
            AcrValues           = authorize.AcrValues,
        };

        await _auths.CreateAsync(authorization, ct);

        properties[Properties.AuthorizationName] = authorization.Name;

        return AuthorizationResult.SignIn(response, properties);
    }

    /// <summary>
    ///     Denies the authorization request by revoking the interaction token.
    ///     No consent record is created.
    /// </summary>
    /// <param name="request">Interaction request containing the reference token code.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction is null) {
            return;
        }

        await _tokens.RevokeAsync(interaction, ct);
    }

    #endregion
}
