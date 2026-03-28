using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

/// <summary>
///     Sends front-channel and back-channel logout notifications per
///     OpenID Connect Front-Channel Logout 1.0 and Back-Channel Logout 1.0.
///     Registered when front-channel or back-channel logout features are enabled.
/// </summary>
public interface ILogoutNotifier
{
    Task<List<string>> GetFrontChannelUrisAsync(string? subject, string? session, CancellationToken ct = default);

    Task EnqueueBackChannelAsync(string? subject, string? session, CancellationToken ct = default);
}
