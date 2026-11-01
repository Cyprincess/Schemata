using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     One mechanism participating in the OP session lifecycle — a host authentication ticket,
///     a persisted continuation, or a browser mirror channel. Adapters are additive: the
///     <c>IOpSessionService</c> implementation collects their evidence, resolves one canonical
///     target, and then persists or clears that target on each applicable adapter.
/// </summary>
/// <remarks>
///     <see cref="ReadAsync" /> returns <see langword="null" /> when the mechanism holds no
///     evidence (or is not applicable, e.g. no ambient HTTP request for a browser channel).
///     <see cref="PersistAsync" /> and <see cref="ClearAsync" /> are invoked for the resolved
///     canonical target only; an adapter that does not own or represent the target no-ops.
///     Instances execute in ascending <see cref="Order" />.
/// </remarks>
public interface IOpSessionStore
{
    /// <summary>Execution order across adapters; lower runs first.</summary>
    int Order { get; }

    /// <summary>
    ///     Reads the evidence this mechanism currently holds, or <see langword="null" /> when
    ///     it holds none or is not applicable in this context.
    /// </summary>
    Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);

    Task<LogoutSessionEvidence?> ReadLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    ///     Persists the canonical session identifier so this mechanism mirrors the target.
    ///     No-op when the mechanism is not applicable.
    /// </summary>
    Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);

    /// <summary>
    ///     Clears the state this mechanism holds for the canonical target. No-op when the
    ///     mechanism neither owns nor mirrors the target.
    /// </summary>
    Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);
}
