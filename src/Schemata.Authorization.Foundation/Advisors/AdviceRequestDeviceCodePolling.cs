using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceRequestDeviceCodePolling" />.</summary>
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
/// <typeparam name="TToken">The token entity type.</typeparam>
public sealed class AdviceRequestDeviceCodePolling<TApp, TToken>(
    [FromKeyedServices(SecurityConstants.TokenTypes.RateSlot)] ITokenStore<TToken> slots,
    ITokenStore<TToken>                                     tokens,
    IOptions<SchemataAuthorizationOptions>                         options
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
    where TToken : SchemataToken, new()
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceRequestDeviceCodePolling.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext             ctx,
        TokenRequestContext<TApp> request,
        CancellationToken         ct = default
    ) {
        var payloadRequest = request.Request;
        if (payloadRequest is null || payloadRequest.GrantType != GrantTypes.DeviceCode) {
            return AdviseResult.Continue;
        }

        var device = payloadRequest.DeviceCode;
        if (string.IsNullOrWhiteSpace(device)) {
            return AdviseResult.Continue;
        }

        var grant = await tokens.FindByReferenceIdAsync(device, ct);
        if (grant?.Type != TokenTypes.DeviceCode) {
            return AdviseResult.Continue;
        }

        // The grown interval is grant-lifetime policy state: it persists on the device grant
        // row (RFC 8628 §3.5 raises the interval for every subsequent request), while the
        // rate slot below is only the transient one-per-window admission ticket.
        var interval = int.TryParse(grant.Value, out var grown) && grown > 0
            ? grown
            : options.Value.DeviceCodeInterval;

        var ticket   = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var admitted = await slots.GetOrCreateAsync(
            null, "device", $"rate:{device}", ticket, TimeSpan.FromSeconds(interval), ct);
        if (string.Equals(admitted.Value, ticket, StringComparison.Ordinal)) {
            return AdviseResult.Continue;
        }

        var next = interval + 5;
        grant.Value = next.ToString();
        try {
            await tokens.UpdateAsync(grant, ct);
        } catch (AbortedException exception) when (SchemataTokenConflicts.IsConflict(exception)) {
            // Another poll lost the same race and already persisted a wider interval;
            // every unrelated abort propagates.
        }

        throw new OAuthException(OAuthErrors.SlowDown, SchemataResources.SLOW_DOWN);
    }

    #endregion
}
