using System;
using System.Collections.Generic;
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
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

public sealed class LogoutInteractionHandler<TApp>(
    ITokenStore<SchemataToken> tokens,
    EndSessionHandler<TApp> endSession,
    IOptions<JsonSerializerOptions> json,
    IOptions<SchemataAuthorizationOptions> options,
    TimeProvider? time = null,
    IOpSessionService? sessions = null
) : IInteractionHandler where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    public string CodeType => TokenTypeUris.Logout;

    public async Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest request, string issuer, CancellationToken ct) {
        var (_, payload) = await ReadAsync(request, ct);
        return AuthorizationResult.Content(new InteractionResponse { Type = "logout" });
    }

    public async Task<AuthorizationResult> ApproveAsync(
        InteractRequest request, ClaimsPrincipal principal, string issuer, CancellationToken ct) {
        if (principal.Identity?.IsAuthenticated != true) {
            throw new OAuthException(OAuthErrors.LoginRequired, SchemataResources.USER_AUTHENTICATION_REQUIRED);
        }
        var (token, payload) = await ReadAsync(request, ct);
        var subject = principal.FindFirstValue(IdentityClaims.Subject);
        var session = principal.FindFirstValue(options.Value.SessionIdClaimType);
        if (sessions is not null) session = await sessions.ResolveAsync(principal, subject, ct);
        if (string.IsNullOrWhiteSpace(payload.Target.Subject)
            || !string.Equals(subject, payload.Target.Subject, StringComparison.Ordinal)
            || !string.Equals(session, payload.Target.SessionId, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        }
        if (!await tokens.TryRedeemAsync(token, ct)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        }
        token.Status = TokenStatuses.Revoked;
        await tokens.UpdateAsync(token, ct);
        return await endSession.ExecuteApprovedAsync(
            payload.Request, principal, payload.Target, ct);
    }

    public async Task DenyAsync(InteractRequest request, CancellationToken ct) {
        var (token, _) = await ReadAsync(request, ct);
        await tokens.RevokeAsync(token, ct);
    }

    private async Task<(SchemataToken Token, LogoutConfirmationPayload Payload)> ReadAsync(
        InteractRequest request, CancellationToken ct) {
        var token = await tokens.FindByReferenceIdAsync(request.Code, ct);
        if (token?.Type != TokenTypes.Logout || token.Status != TokenStatuses.Valid
            || token.ExpireTime is { } expiry && expiry <= _time.GetUtcNow().UtcDateTime
            || string.IsNullOrWhiteSpace(token.Payload)) {
            throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        }
        var payload = JsonSerializer.Deserialize<LogoutConfirmationPayload>(token.Payload, json.Value)
            ?? throw new OAuthException(OAuthErrors.InvalidRequest, SchemataResources.INVALID_REQUEST);
        return (token, payload);
    }
}
