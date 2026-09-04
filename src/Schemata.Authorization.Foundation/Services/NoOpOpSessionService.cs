using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Neutral no-op default for <see cref="IOpSessionService" />, used when no host session
///     integration is registered.
/// </summary>
public sealed class NoOpOpSessionService : IOpSessionService
{
    #region IOpSessionService Members

    public Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default) {
        return Task.CompletedTask;
    }

    #endregion
}