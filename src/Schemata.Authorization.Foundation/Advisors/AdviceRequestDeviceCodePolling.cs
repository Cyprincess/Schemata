using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRequestDeviceCodePolling{TApp}" />.</summary>
public static class AdviceRequestDeviceCodePolling
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceRequestGrantPermission.DefaultOrder + 10_000_000;
}

/// <summary>
///     Rate-limits device token polling at the token endpoint,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.4">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.4: Device Access Token Request
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8628.html#section-3.5">
///         RFC 8628: OAuth 2.0 Device Authorization
///         Grant §3.5: Device Access Token Response
///     </seealso>
///     .
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
public sealed class AdviceRequestDeviceCodePolling<TApp>(
    [FromKeyedServices(SecurityConstants.TokenTypes.RateSlot)] ITokenStore<SchemataToken> slots,
    IOptions<SchemataAuthorizationOptions>                                     options
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceRequestDeviceCodePolling.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TApp              application,
        TokenRequest      request,
        CancellationToken ct = default
    ) {
        if (request.GrantType != GrantTypes.DeviceCode) {
            return AdviseResult.Continue;
        }

        var device = request.DeviceCode;
        if (string.IsNullOrWhiteSpace(device)) {
            return AdviseResult.Continue;
        }

        var existing = await slots.GetAsync(null, "device", $"rate:{device}", ct);
        if (existing is not null) {
            // RFC 8628 §3.5: the client MUST raise its polling interval by 5 seconds on every
            // slow_down. The grown interval is persisted so repeated too-fast polls widen the
            // enforced window for this device code.
            var current = int.TryParse(existing.Value, out var parsed) ? parsed : options.Value.DeviceCodeInterval;
            var next    = current + 5;
            await slots.SetAsync(null, "device", $"rate:{device}", next.ToString(), TimeSpan.FromSeconds(next), ct);

            throw new OAuthException(
                OAuthErrors.SlowDown,
                SchemataResources.GetResourceString(SchemataResources.SLOW_DOWN)
            );
        }

        await slots.GetOrCreateAsync(
            null,
            "device",
            $"rate:{device}",
            options.Value.DeviceCodeInterval.ToString(),
            TimeSpan.FromSeconds(options.Value.DeviceCodeInterval),
            ct);

        return AdviseResult.Continue;
    }

    #endregion
}
