using System;
using System.Globalization;
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
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Extensions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the consent/login interaction flow for the OAuth 2.0 authorization endpoint.
///     An SPA calls GET to render the consent screen and POST to approve or deny.
///     Implements <see cref="IInteractionHandler" /> for <see cref="TokenTypeUris.Interaction" />.
/// </summary>
public sealed class AuthorizeInteractionHandler<TApp, TAuth, TScope> : IInteractionHandler
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
{
    private readonly IApplicationManager<TApp>              _apps;
    private readonly IAuthorizationManager<TAuth>           _auths;
    private readonly IAuthenticationContextProvider?        _contexts;
    private readonly IOptions<JsonSerializerOptions>        _json;
    private readonly IOptions<SchemataAuthorizationOptions> _options;
    private readonly IScopeManager<TScope>                  _scopes;
    private readonly TimeProvider                           _time;
    private readonly IOpSessionService? _sessions;
    private readonly ITokenStore<SchemataToken>                    _tokens;
    private readonly IEnumerable<IAuthorizeAdvisor<TApp>>?  _advisors;

    /// <summary>
    ///     Initializes the handler with the required managers and configuration.
    /// </summary>
    /// <param name="apps">Application registry.</param>
    /// <param name="auths">Authorization storage for consent records.</param>
    /// <param name="scopes">Scope resolver.</param>
    /// <param name="tokens">Token storage.</param>
    /// <param name="json">JSON serialization options.</param>
    /// <param name="options">Server-level authorization configuration.</param>
    /// <param name="time">Clock used for interaction-token expiry; defaults to the system clock.</param>
    /// <param name="sessions">Optional OP-session authority.</param>
    /// <param name="contexts">Optional host authentication-event resolver.</param>
    /// <param name="advisors">
    ///     The installed authorize-advisor stage; the claims-parameter approval rule runs only
    ///     when its advisor is part of the stage.
    /// </param>
    public AuthorizeInteractionHandler(
        IApplicationManager<TApp>              apps,
        IAuthorizationManager<TAuth>           auths,
        IScopeManager<TScope>                  scopes,
        ITokenStore<SchemataToken>                    tokens,
        IOptions<JsonSerializerOptions>        json,
        IOptions<SchemataAuthorizationOptions> options,
        TimeProvider?                          time     = null,
        IOpSessionService? sessions = null,
        IAuthenticationContextProvider?        contexts = null,
        IEnumerable<IAuthorizeAdvisor<TApp>>?   advisors = null
    ) {
        _apps     = apps;
        _auths    = auths;
        _contexts = contexts;
        _sessions = sessions;
        _scopes   = scopes;
        _tokens   = tokens;
        _json     = json;
        _options  = options;
        _time     = time ?? TimeProvider.System;
        _advisors = advisors;
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
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    ) {
        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction?.Status != TokenStatuses.Valid
         || interaction.Type != TokenTypes.Interaction
            || (interaction.ExpireTime.HasValue && interaction.ExpireTime.Value <= _time.GetUtcNow().UtcDateTime)
         || string.IsNullOrWhiteSpace(interaction.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var clear = interaction.Payload;

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(clear, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var application = await _apps.FindByClientIdAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var requested = ScopeParser.Parse(authorize.Scope);

        var scopes = await _scopes.ResolveScopesAsync(requested, ct)
                                  .Map(s => {
                                       var scope = new ScopeResponse { Name = s.Name };
                                       s.CopyLabels(scope);
                                       return scope;
                                   }, ct).ToListAsync(ct);

        // Re-resolve response_mode from response_type when it was not explicitly
        // provided, so the SPA can display the correct callback method.
        if (!string.IsNullOrWhiteSpace(authorize.ResponseType)) {
            authorize.ResponseMode = ResponseModeService.ResolveMode(authorize.ResponseMode, authorize.ResponseType);
        }

        var client = new ApplicationResponse { ClientId = application.ClientId };
        application.CopyLabels(client);

        return AuthorizationResult.Content(new InteractionResponse {
            Type        = InteractionTypes.Authorize,
            Request     = authorize,
            Application = client,
            Scopes      = scopes,
        });
    }

    /// <summary>
    ///     Approves the authorization request: revokes the interaction token,
    ///     creates a consent record (<typeparamref name="TAuth" />), and returns
    ///     a <see cref="AuthorizationResult.SignIn" /> carrying all auth properties
    ///     needed by <see cref="SchemataAuthorizationCodeHandler{TApp}" />.
    /// </summary>
    /// <param name="request">Interaction request containing the reference token code.</param>
    /// <param name="principal">The authenticated resource owner.</param>
    /// <param name="issuer">Token issuer URI.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    ) {
        // The interaction page calls this endpoint over XHR; a cookie challenge would answer with a
        // redirect the caller cannot follow, so an unauthenticated approval is a plain 401.
        var subject = principal.FindFirstValue(IdentityClaims.Subject);
        if (string.IsNullOrWhiteSpace(subject)) {
            throw new UnauthenticatedException(SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }

        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction?.Status != TokenStatuses.Valid
         || interaction.Type != TokenTypes.Interaction
            || (interaction.ExpireTime.HasValue && interaction.ExpireTime.Value <= _time.GetUtcNow().UtcDateTime)
         || string.IsNullOrWhiteSpace(interaction.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var clear = interaction.Payload;

        var authorize = JsonSerializer.Deserialize<AuthorizeRequest>(clear, _json.Value);
        if (string.IsNullOrWhiteSpace(authorize?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var application = await _apps.FindByClientIdAsync(authorize.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.INVALID_GRANT);
        }

        var claims = new List<Claim> {
            new(IdentityClaims.Subject, subject),
            new(Claims.ClientId, application.ClientId),
        };

        var sid = principal.FindFirstValue(_options.Value.SessionIdClaimType);
        if (string.IsNullOrWhiteSpace(sid)
         && !string.IsNullOrWhiteSpace(authorize.OpSessionId)
         && string.Equals(authorize.OpSessionSubject, subject, StringComparison.Ordinal)) {
            // Continuation lineage: the authorize leg issued this session for the SAME subject;
            // an authorize-as-A / approve-as-B switch falls through and mints B's own session.
            sid = authorize.OpSessionId;
        }

        if (string.IsNullOrWhiteSpace(sid) && _sessions is not null) {
            // Anonymous authorize, then login: the approval leg carries the first real host
            // principal, so the session is established here rather than at the synthetic
            // callback principal.
            sid = await _sessions.IssueAsync(principal, subject, ct);
        }

        if (!string.IsNullOrWhiteSpace(sid)) {
            claims.Add(new(_options.Value.SessionIdClaimType, sid));
        }

        var response = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        var mode     = ResponseModeService.ResolveMode(authorize.ResponseMode, authorize.ResponseType);

        // The grant set was validated at the authorize leg and stamped onto the interaction
        // payload as normalized JSON; it is null when the feature is absent.
        var details = string.IsNullOrWhiteSpace(authorize.AuthorizationDetails) ? null : authorize.AuthorizationDetails;

        // Inherit the authorize-leg event only when the approval continues that authentication:
        // same subject on the same OP session lineage the evidence was resolved under. A payload
        // without lineage or a rotated SID resolves the approving principal's own evidence.
        var inheritedAuthentication = string.Equals(authorize.AuthenticationSubject, subject, StringComparison.Ordinal)
                                   && !string.IsNullOrWhiteSpace(authorize.OpSessionId)
                                   && string.Equals(authorize.OpSessionId, sid, StringComparison.Ordinal)
            ? authorize.Authentication
            : null;
        var authentication = inheritedAuthentication
                          ?? await AuthenticationContextExtensions.ResolveAsync(
                              principal,
                              null,
                              sid,
                              _options.Value.SessionIdClaimType,
                              _contexts,
                              ct);

        // The requirements attached to the authorize request bind the FINAL authentication: an
        // approval whose resolved event does not satisfy them is rejected as a JSON OAuth error
        // before any code, grant, or consent record is created.
        if (application.RequireAuthTime && authentication?.AuthTime is null) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }

        if (!string.IsNullOrWhiteSpace(authorize.MaxAge)) {
            if (!int.TryParse(authorize.MaxAge, NumberStyles.None, CultureInfo.InvariantCulture, out var age) || age < 0) {
                throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.NOT_SUPPORTED, new Dictionary<string, string?> { ["value"] = Parameters.MaxAge });
            }

            var fresh = authentication?.AuthTime is { } epoch
                     && (authorize.AuthenticationRequiredAfter is { } boundary
                         ? epoch >= boundary
                         : _time.GetUtcNow().ToUnixTimeSeconds() - epoch <= age);
            if (!fresh) {
                throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
            }
        }

        if (_advisors?.OfType<AdviceAuthorizeClaims<TApp>>().Any() == true) {
            AdviceAuthorizeClaims.ValidateApproval(authorize.Claims, authentication?.Acr, subject);
        }

        var grant = AuthorizationGrantContexts.Create(
            subject, authorize.Scope, sid, GrantTypes.AuthorizationCode, authentication,
            authorize.GrantProfile);

        var properties = new Dictionary<string, string?> {
            [Properties.GrantType]           = GrantTypes.AuthorizationCode,
            [Properties.Scope]               = authorize.Scope,
            [Properties.Resources]           = authorize.Resource is { Count: > 0 } ? string.Join(" ", authorize.Resource) : null,
            [Properties.ResponseType]        = authorize.ResponseType,
            [Properties.Nonce]               = authorize.Nonce,
            [Properties.RedirectUri]         = authorize.RedirectUri,
            [Properties.ResponseMode]        = mode,
            [Properties.State]               = authorize.State,
            [Properties.CodeChallenge]       = authorize.CodeChallenge,
            [Properties.CodeChallengeMethod] = authorize.CodeChallengeMethod,
            [Properties.DpopJkt]             = authorize.DpopJkt,
            [Properties.SessionId]           = sid,
            [Properties.GrantContext]        = AuthorizationGrantContexts.Serialize(grant),
            [Properties.GrantProfile]        = grant.Profile,
            [Properties.MaxAge]              = authorize.MaxAge,
            [Properties.ClaimsRequest]       = authorize.Claims,
            [Properties.SessionStateSalt]    = authorize.SessionStateSalt,
        };

        await _tokens.RevokeAsync(interaction, ct);

        // Record consent so future requests for the same client/scope can skip interaction.
        // Application / AuthorizationName carry full AIP-122 canonical names per the
        // [ResourceReference] contracts on SchemataAuthorization / SchemataToken; the OAuth
        // wire `client_id` is resolved independently through FindByClientIdAsync.
        var authorization = new TAuth {
            Application          = application.CanonicalName,
            Subject              = subject,
            Type                 = AuthorizationTypes.AdHoc,
            Status               = TokenStatuses.Valid,
            Scopes               = authorize.Scope,
            RedirectUri          = authorize.RedirectUri,
            ResponseType         = authorize.ResponseType,
            CodeChallengeMethod  = authorize.CodeChallengeMethod,
            AcrValues            = authorize.AcrValues,
            AuthorizationDetails = details,
        };

        await _auths.CreateAsync(authorization, ct);

        properties[Properties.AuthorizationName] = authorization.CanonicalName;
        properties[Properties.AuthorizationDetails] = details;

        return AuthorizationResult.SignIn(response, properties);
    }

    /// <summary>
    ///     Denies the authorization request by revoking the interaction token.
    ///     No consent record is created.
    /// </summary>
    /// <param name="request">Interaction request containing the reference token code.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var interaction = await _tokens.FindByReferenceIdAsync(request.Code, ct);
        if (interaction is null) {
            return;
        }

        await _tokens.RevokeAsync(interaction, ct);
    }

    #endregion
}
