using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Default OP session adapter: issuance is neutral and invalidation delegates to the host
///     session terminator when one is registered.
/// </summary>
public sealed class DefaultOpSessionService(IOpSessionTerminator? terminator = null) : IOpSessionService
{

    public Task<string?> IssueAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.FromResult<string?>(null);
    }
    #region IOpSessionService Members

    public Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default) {
        return terminator?.TerminateAsync(principal, subject, sessionId, ct) ?? Task.CompletedTask;
    }

    #endregion
}