using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Handles the <c>urn:ietf:params:oauth:grant-type:device_code</c> grant type.
///     Validates the device code token, runs the
///     <see cref="IDeviceCodeExchangeAdvisor{TApp,TToken}" /> pipeline, enforces
///     scope constraints, and revokes the device code on success,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.4: Device Access Token Request
///     </seealso>
///     .
/// </summary>
public sealed class DeviceCodeHandler<TApp, TToken>(
    IClientAuthenticationService<TApp> client,
    ITokenManager<TToken>              tokens,
    IServiceProvider                   sp,
    IOptions<JsonSerializerOptions>    json
) : IGrantHandler
    where TApp : SchemataApplication
    where TToken : SchemataToken
{
    #region IGrantHandler Members

    /// <inheritdoc cref="IGrantHandler.GrantType" />
    public string GrantType => GrantTypes.DeviceCode;

    /// <summary>
    ///     Polls for device authorization completion and, when the device code
    ///     is in the <see cref="TokenStatuses.Authorized" /> state, issues tokens
    ///     and revokes the device code to prevent replay,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
    ///         RFC 8628: OAuth 2.0 Device Authorization
    ///         Grant §3.4: Device Access Token Request
    ///     </seealso>
    ///     .
    /// </summary>
    /// <param name="request">Token request containing the device code.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    ) {
        if (string.IsNullOrWhiteSpace(request.DeviceCode)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), Parameters.DeviceCode)
            );
        }

        var application = await client.AuthenticateAsync(null, new(){
            [Parameters.ClientId]     = [request.ClientId],
            [Parameters.ClientSecret] = [request.ClientSecret],
        }, headers, ct);
        if (string.IsNullOrWhiteSpace(application?.ClientId)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.ST4001)
            );
        }

        var ctx = new AdviceContext(sp);

        switch (await Advisor.For<ITokenRequestAdvisor<TApp>>()
                             .RunAsync(ctx, application, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.ST4001)
                );
        }

        var token = await tokens.FindByReferenceIdAsync(request.DeviceCode, ct);
        if (token is null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var exchange = new DeviceCodeExchangeContext<TApp, TToken> {
            Request     = request,
            Application = application,
            Token       = token,
        };

        switch (await Advisor.For<IDeviceCodeExchangeAdvisor<TApp, TToken>>()
                             .RunAsync(ctx, exchange, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ST4008)
                );
        }

        if (string.IsNullOrWhiteSpace(token.Payload)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var payload = JsonSerializer.Deserialize<DeviceCodePayload>(token.Payload, json.Value);
        if (payload is null) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.ST4004)
            );
        }

        var scope = payload.Scope;
        if (!string.IsNullOrWhiteSpace(request.Scope)) {
            if (!ScopeParser.IsSubset(request.Scope, payload.Scope)) {
                throw new OAuthException(
                    OAuthErrors.InvalidScope,
                    SchemataResources.GetResourceString(SchemataResources.ST4006)
                );
            }

            scope = request.Scope;
        }

        // device codes are single-use; revoke on successful exchange.
        // See RFC 8628 §3.4.
        await tokens.RevokeAsync(token, ct);

        var claims = new List<Claim> {
            new(Claims.Subject, token.Subject!),
            new(Claims.ClientId, application.ClientId),
        };

        var identity = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemataAuthorizationSchemes.Bearer));
        return AuthorizationResult.SignIn(identity, new() {
            [Properties.GrantType]         = GrantTypes.DeviceCode,
            [Properties.Scope]             = scope,
            [Properties.AuthorizationName] = token.AuthorizationName,
            [Properties.SessionId]         = token.SessionId,
        });
    }

    #endregion
}
