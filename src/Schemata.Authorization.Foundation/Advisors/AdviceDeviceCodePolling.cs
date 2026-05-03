using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceDeviceCodePolling{TApp}" />.</summary>
public static class AdviceDeviceCodePolling
{
    public const int DefaultOrder = AdviceTokenGrantPermission.DefaultOrder + 10_000_000;
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
public sealed class AdviceDeviceCodePolling<TApp>(ICacheProvider cache, IOptions<SchemataAuthorizationOptions> options) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => AdviceDeviceCodePolling.DefaultOrder;

    /// <inheritdoc />
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

        var key = $"polling\x1e{device}".ToCacheKey(Keys.Authorization);
        if (await cache.GetAsync(key, ct) is not null) {
            throw new OAuthException(
                OAuthErrors.SlowDown,
                SchemataResources.GetResourceString(SchemataResources.ST4013)
            );
        }

        await cache.SetAsync(key, [], new() {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.DeviceCodeInterval),
        }, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
