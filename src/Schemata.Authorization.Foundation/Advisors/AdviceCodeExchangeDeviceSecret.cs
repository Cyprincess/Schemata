using System;
using System.Text.Json;
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
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceCodeExchangeDeviceSecret<TApp> : ICodeExchangeAdvisor<TApp>
    where TApp : SchemataApplication
{
    private readonly IApplicationManager<TApp> _apps;
    private readonly ITokenStore<SchemataToken> _tokens;
    private readonly IDeviceIdResolver _devices;
    private readonly NativeSingleSignOnOptions _options;
    private readonly TimeProvider _time;

    public AdviceCodeExchangeDeviceSecret(
        IApplicationManager<TApp>              apps,
        ITokenStore<SchemataToken>             tokens,
        IDeviceIdResolver                      devices,
        IOptions<NativeSingleSignOnOptions> options,
        TimeProvider?                          time = null
    ) {
        _apps    = apps;
        _tokens  = tokens;
        _devices = devices;
        _options = options.Value;
        _time    = time ?? TimeProvider.System;
    }

    public int Order => 900_000_000 - 20_000_000;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        CodeExchangeContext<TApp> exchange,
        CancellationToken         ct = default
    ) {
        var request = exchange.Request;
        var client  = exchange.Application;
        var scope   = request?.Scope ?? exchange.Payload?.Scope;
        if (request is null || client is null || string.IsNullOrWhiteSpace(scope)) {
            return AdviseResult.Continue;
        }
        if (!ScopeParser.Contains(scope, Scopes.DeviceSso)) {
            return AdviseResult.Continue;
        }

        if (!ScopeParser.Contains(scope, Scopes.OpenId)
            || !await _apps.HasPermissionAsync(client, PermissionPrefixes.Scope + Scopes.DeviceSso, ct)) {
            throw new OAuthException(
                OAuthErrors.InvalidScope,
                SchemataResources.GetResourceString(SchemataResources.INVALID_SCOPE));
        }

        var sid    = exchange.CodeToken?.SessionId;
        if (string.IsNullOrWhiteSpace(sid)) {
            throw new OAuthException(
                OAuthErrors.InvalidGrant,
                SchemataResources.GetResourceString(SchemataResources.INVALID_GRANT));
        }
        var source = SecurityParents.Application(client);
        var device = await _devices.ResolveAsync(null, exchange.CodeToken?.Parent, ct);

        // §3.4: invalid supplied secret is treated as absent. If a valid one is supplied, reuse it.
        if (!string.IsNullOrWhiteSpace(request.DeviceSecret)) {
            var existing = await _tokens.FindByReferenceIdAsync(request.DeviceSecret, ct);
            if (existing is not null
                && DeviceSecretBinding.IsUsable(existing, client, sid, device, _time.GetUtcNow().UtcDateTime)) {
                ctx.Set(new DeviceSecretIssuance(
                    existing.ReferenceId!, existing.DeviceId, existing.Application, existing.SessionId));
                return AdviseResult.Continue;
            }
        }

        var secret = await IssueAsync(source, device, sid, ct);
        ctx.Set(new DeviceSecretIssuance(secret, device, source, sid));
        return AdviseResult.Continue;
    }

    private async Task<string> IssueAsync(string? sourceClientId, string? deviceId, string? sessionId, CancellationToken ct) {
        var reference = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var now       = _time.GetUtcNow().UtcDateTime;
        var payload   = JsonSerializer.Serialize(new {
            client = sourceClientId,
            device = deviceId,
            sid    = sessionId,
            ts     = now,
        });
        await _tokens.CreateAsync(new SchemataToken {
            Type        = TokenTypes.DeviceSecret,
            Status      = TokenStatuses.Valid,
            Format      = TokenFormats.Reference,
            ReferenceId = reference,
            Payload     = payload,
            Application = sourceClientId,
            SessionId   = sessionId,
            DeviceId    = deviceId,
            ExpireTime  = now + _options.DeviceSecretLifetime,
        }, ct);
        return reference;
    }
}