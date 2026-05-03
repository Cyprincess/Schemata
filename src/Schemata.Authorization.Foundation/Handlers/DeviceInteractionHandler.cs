using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Extensions;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles device-code consent/login interaction for the OAuth 2.0 Device
///     Authorization flow per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html">RFC 8628: OAuth 2.0 Device Authorization Grant</seealso>
///     .
///     An SPA calls GET to render the device
///     consent screen and POST to approve or deny.
///     Implements <see cref="IInteractionHandler" /> for <see cref="TokenTypeUris.UserCode" />.
/// </summary>
public sealed class DeviceInteractionHandler<TApp, TAuth, TScope, TToken>(
    IApplicationManager<TApp>              apps,
    ITokenManager<TToken>                  tokens,
    IScopeManager<TScope>                  scopes,
    IAuthorizationManager<TAuth>           auths,
    IOptions<SchemataAuthorizationOptions> options,
    IOptions<JsonSerializerOptions>        json
) : IInteractionHandler
    where TApp : SchemataApplication
    where TAuth : SchemataAuthorization, new()
    where TScope : SchemataScope
    where TToken : SchemataToken
{
    #region IInteractionHandler Members

    /// <inheritdoc cref="IInteractionHandler.CodeType" />
    public string CodeType => TokenTypeUris.UserCode;

    /// <summary>
    ///     Returns the device consent details: the client application metadata
    ///     and requested scopes resolved from the device code payload.
    /// </summary>
    /// <param name="request">Interaction request containing the user code.</param>
    /// <param name="issuer">Token issuer URI.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    ) {
        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (token?.Status != TokenStatuses.Valid
         || token.Type != TokenTypes.UserCode
         || (token.ExpireTime.HasValue && token.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(token.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var uc = JsonSerializer.Deserialize<UserCodePayload>(token.Payload, json.Value);
        if (string.IsNullOrWhiteSpace(uc?.DeviceCodeName)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var device = await tokens.FindByCanonicalNameAsync(uc.DeviceCodeName, ct);
        if (device?.Status != TokenStatuses.Valid
         || device.Type != TokenTypes.DeviceCode
         || (device.ExpireTime.HasValue && device.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(device.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var payload = JsonSerializer.Deserialize<DeviceCodePayload>(device.Payload, json.Value);
        if (string.IsNullOrWhiteSpace(payload?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var application = await apps.FindByCanonicalNameAsync(payload.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var requested = ScopeParser.Parse(payload.Scope);

        var list = await scopes.ResolveScopesAsync(requested, ct)
                               .Map(s => new ScopeResponse {
                                    Name         = s.Name,
                                    DisplayName  = s.DisplayName,
                                    DisplayNames = s.DisplayNames,
                                    Description  = s.Description,
                                    Descriptions = s.Descriptions,
                                }, ct).ToListAsync(ct);

        return AuthorizationResult.Content(new InteractionResponse {
            Type = InteractionTypes.Device,
            Application = new() {
                ClientId     = payload.ClientId,
                DisplayName  = application.DisplayName,
                DisplayNames = application.DisplayNames,
            },
            Scopes = list,
        });
    }

    /// <summary>
    ///     Approves the device authorization request: updates the device code
    ///     status to <see cref="TokenStatuses.Authorized" />, attaches the subject
    ///     and authorization reference, then revokes the user code.
    ///     Returns HTTP 204 via <see cref="NoContentException" />.
    /// </summary>
    /// <param name="request">Interaction request containing the user code.</param>
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
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4017));
        }

        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (token?.Status != TokenStatuses.Valid
         || token.Type != TokenTypes.UserCode
         || (token.ExpireTime.HasValue && token.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(token.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var uc = JsonSerializer.Deserialize<UserCodePayload>(token.Payload, json.Value);
        if (string.IsNullOrWhiteSpace(uc?.DeviceCodeName)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var device = await tokens.FindByCanonicalNameAsync(uc.DeviceCodeName, ct);
        if (device?.Status != TokenStatuses.Valid
         || device.Type != TokenTypes.DeviceCode
         || (device.ExpireTime.HasValue && device.ExpireTime.Value <= DateTime.UtcNow)
         || string.IsNullOrWhiteSpace(device.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var payload = JsonSerializer.Deserialize<DeviceCodePayload>(device.Payload, json.Value);
        if (string.IsNullOrWhiteSpace(payload?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var application = await apps.FindByCanonicalNameAsync(payload.ClientId, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var authorization = new TAuth {
            ApplicationName = application.Name,
            Subject         = subject,
            Type            = AuthorizationTypes.Device,
            Status          = TokenStatuses.Valid,
            Scopes          = payload.Scope,
        };
        await auths.CreateAsync(authorization, ct);

        var sid = principal.FindFirstValue(options.Value.SessionIdClaimType);

        device.Subject           = subject;
        device.Status            = TokenStatuses.Authorized;
        device.AuthorizationName = authorization.Name;
        device.SessionId         = sid;

        await tokens.UpdateAsync(device, ct);
        await tokens.RevokeAsync(token, ct);

        throw new NoContentException();
    }

    /// <summary>
    ///     Denies the device authorization request: sets the device code status
    ///     to <see cref="TokenStatuses.Denied" /> and revokes the user code.
    /// </summary>
    /// <param name="request">Interaction request containing the user code.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (token?.Type != TokenTypes.UserCode || string.IsNullOrWhiteSpace(token.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var uc = JsonSerializer.Deserialize<UserCodePayload>(token.Payload, json.Value);
        if (string.IsNullOrWhiteSpace(uc?.DeviceCodeName)) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        var device = await tokens.FindByCanonicalNameAsync(uc.DeviceCodeName, ct);
        if (device?.Type != TokenTypes.DeviceCode) {
            throw new OAuthException(OAuthErrors.InvalidGrant, SchemataResources.GetResourceString(SchemataResources.ST4004));
        }

        device.Status = TokenStatuses.Denied;

        await tokens.UpdateAsync(device, ct);
        await tokens.RevokeAsync(token, ct);
    }

    #endregion
}
