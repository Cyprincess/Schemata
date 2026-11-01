using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton;

/// <summary>Sends prepared front-channel and back-channel logout notifications.</summary>
public interface ILogoutNotifier
{
    /// <summary>Captures relying-party notification evidence before session invalidation.</summary>
    Task<LogoutNotificationSnapshot> PrepareAsync(
        string? subject, string? session, CancellationToken ct = default);

    /// <summary>Dispatches a previously captured snapshot after successful invalidation.</summary>
    Task DispatchAsync(LogoutNotificationSnapshot snapshot, CancellationToken ct = default);
}