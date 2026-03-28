using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceDeviceCodePolling
{
    public const int DefaultOrder = AdviceTokenGrantPermission.DefaultOrder + 10_000_000;
}

public sealed class AdviceDeviceCodePolling<TApp>(
    IDistributedCache                      cache,
    IOptions<SchemataAuthorizationOptions> options
) : ITokenRequestAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region ITokenRequestAdvisor<TApp> Members

    public int Order => AdviceDeviceCodePolling.DefaultOrder;

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

        var key = $"{Keys.DevicePoll}:{device}";
        if (await cache.GetAsync(key, ct) is not null) {
            throw new OAuthException(OAuthErrors.SlowDown, SchemataResources.GetResourceString(SchemataResources.ST4013));
        }

        await cache.SetAsync(key, [1], new() {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.DeviceCodeInterval),
        }, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
