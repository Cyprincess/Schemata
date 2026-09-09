using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Resolves a pushed authorization request by its <c>request_uri</c> handle,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-4">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §4: Authorization Request
///     </seealso>
///     . Runs first in the authorize pipeline so the recovered parameters reach the rest of
///     the advisors and the grant handler. Enforces
///     <see cref="PushedAuthorizationRequestsOptions.RequireForAllClients" /> when
///     no <c>request_uri</c> is present.
/// </summary>
public sealed class AdviceAuthorizeRequestUri<TApp>(
    IApplicationManager<TApp>              apps,
    ITokenStore<SchemataToken>             tokens,
    IOptions<PushedAuthorizationRequestsOptions> options,
    IOptions<JsonSerializerOptions>        json,
    TimeProvider?                          time = null
) : IAuthorizeRequestAdvisor<TApp>
    where TApp : SchemataApplication
{

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public int Order => AdviceAuthorizeRequestUri.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext          ctx,
        AuthorizeContext<TApp> authz,
        CancellationToken      ct = default
    ) {
        var request = authz.Request;
        var raw     = request?.RequestUri;

        if (!string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(request?.Request)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_OBJECT));
        }

        if (string.IsNullOrWhiteSpace(raw)) {
            if (ctx.Has<ParEndpointValidation>()) {
                return AdviseResult.Continue;
            }

            var requiredApplication = await apps.FindByClientIdAsync(request?.ClientId, ct);
            if (options.Value.RequireForAllClients
                || requiredApplication?.RequirePushedAuthorizationRequests == true) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.RequestUri));
            }

            return AdviseResult.Continue;
        }

        if (!raw.StartsWith(RequestUriPrefixes.Par, StringComparison.Ordinal)) {
            // Spec §4.6: by-reference request_uri from a remote server is not implemented.
            throw new OAuthException(
                OAuthErrors.RequestUriNotSupported,
                SchemataResources.GetResourceString(SchemataResources.REQUEST_URI_NOT_SUPPORTED));
        }

        if (string.IsNullOrWhiteSpace(request?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequest,
                string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), Parameters.ClientId));
        }

        var application = await apps.FindByClientIdAsync(request.ClientId, ct);
        if (application is null) {
            throw new OAuthException(
                OAuthErrors.InvalidRequestUri,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_URI));
        }

        authz.Application = application;

        var reference = raw[RequestUriPrefixes.Par.Length..];
        var stored = await tokens.FindByReferenceIdAsync(reference, ct);
        if (stored is null
            || stored.Type != TokenTypes.ParRequest
            || !string.Equals(stored.Status, TokenStatuses.Valid, StringComparison.Ordinal)
            || stored.ExpireTime <= _time.GetUtcNow().UtcDateTime
            || !string.Equals(stored.Application, SecurityParents.Application(application), StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequestUri,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_URI));
        }

        if (!options.Value.AllowRequestUriReplay) {
            if (!await tokens.TryRedeemAsync(stored, ct)) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequestUri,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_URI));
            }
        }

        var parsed = JsonSerializer.Deserialize<AuthorizeRequest>(stored.Payload!, json.Value);
        if (parsed is null || !string.Equals(parsed.ClientId, request.ClientId, StringComparison.Ordinal)) {
            throw new OAuthException(
                OAuthErrors.InvalidRequestUri,
                SchemataResources.GetResourceString(SchemataResources.INVALID_REQUEST_URI));
        }

        parsed.RequestUri  = raw;
        authz.Request      = parsed;
        authz.ResponseMode = ResponseModeService.ResolveMode(parsed.ResponseMode, parsed.ResponseType);
        ctx.Set(new ParRequestResolved());

        return AdviseResult.Continue;
    }
}

/// <summary>Order constants for <see cref="AdviceAuthorizeRequestUri{TApp}" />.</summary>
public static class AdviceAuthorizeRequestUri
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeClientAndRedirect.DefaultOrder - 2_000;
}

internal sealed class ParEndpointValidation { }

internal sealed class ParRequestResolved { }