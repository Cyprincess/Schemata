using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     OIDC Back-Channel Logout HTTP POST per
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>.
///     Best-effort by spec: failures are logged, not thrown.
/// </summary>
public sealed class BackChannelLogoutJob(
    IHttpClientFactory            factory,
    ILogger<BackChannelLogoutJob> logger
) : IScheduledJob
{
    #region IScheduledJob Members

    public async Task ExecuteAsync(JobContext context, CancellationToken ct) {
        if (!context.Variables.TryGetValue(VariableKeys.Uri, out var uriRaw) || uriRaw is null) {
            return;
        }

        if (!context.Variables.TryGetValue(VariableKeys.LogoutToken, out var jwtRaw) || jwtRaw is null) {
            return;
        }

        var uri = uriRaw.ToString();
        var jwt = jwtRaw.ToString();

        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(jwt)) {
            return;
        }

        try {
            var client = factory.CreateClient(nameof(BackChannelLogoutService<,>));
            client.Timeout = BackChannelLogoutService.Timeout;
            var content  = new FormUrlEncodedContent([new(Parameters.LogoutToken, jwt)]);
            var response = await client.PostAsync(uri, content, ct);

            if (!response.IsSuccessStatusCode) {
                logger.LogWarning("Back-channel logout to {Uri} returned {StatusCode}.", uri, (int)response.StatusCode);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Back-channel logout to {Uri} failed.", uri);
        }
    }

    #endregion

    internal static class VariableKeys
    {
        public const string Uri         = "uri";
        public const string LogoutToken = "logout_token";
    }
}
