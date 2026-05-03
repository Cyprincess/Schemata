using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Sends front-channel and back-channel logout notifications.
///     Registered when the corresponding logout features are enabled on the authorization server,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-frontchannel-1_0.html">
///         OpenID Connect Front-Channel Logout
///         1.0
///     </seealso>
///     and
///     <seealso href="https://openid.net/specs/openid-connect-backchannel-1_0.html">OpenID Connect Back-Channel Logout 1.0</seealso>
///     .
/// </summary>
public interface ILogoutNotifier
{
    /// <summary>Returns the front-channel logout URIs that should be rendered for the given session.</summary>
    Task<List<string>> GetFrontChannelUrisAsync(string? subject, string? session, CancellationToken ct = default);

    /// <summary>Enqueues a back-channel logout for all client sessions matching the given subject and session.</summary>
    Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default);
}
