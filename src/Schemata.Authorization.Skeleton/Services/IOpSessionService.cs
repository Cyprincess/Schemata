using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     OP session authority, per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">
///         OpenID Connect RP-Initiated Logout 1.0 §2: Logout Request
///     </seealso>
///     .
/// </summary>
public interface IOpSessionService
{

    /// <summary>
    ///     Establishes (or reuses) the OP session for the given principal / subject and returns
    ///     its identifier, per
    ///     <seealso href="https://openid.net/specs/openid-connect-session-1_0.html#OPRequest">
    ///         OpenID Connect Session Management 1.0 §3.2: OP Request
    ///     </seealso>
    ///     . The built-in implementation returns an identifier only from trusted session
    ///     evidence or newly establishes one from trustworthy host authentication. Anonymous and
    ///     synthetic grant principals do not establish a fresh identifier.
    /// </summary>
    Task<string?> IssueAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);

    /// <summary>
    ///     Resolves the canonical OP session identifier for the given principal / subject from
    ///     collected evidence, without minting or persisting anything. Trusted evidence that
    ///     disagrees fails closed; returns <see langword="null" /> when no evidence names a
    ///     session.
    /// </summary>
    Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);

    Task<LogoutSessionEvidence?> ResolveLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    ///     Establishes (or reuses) the online OP-session authority for the subject/session pair and
    ///     returns its opaque generation. A live slot keeps its generation and remaining lifetime;
    ///     an absent or expired slot is atomically replaced with a fresh generation, so a
    ///     reauthorization after expiry or logout never revives grants born under an older
    ///     generation. Returns <see langword="null" /> when no persistent slot store is available;
    ///     online issuance then fails closed.
    /// </summary>
    Task<string?> EstablishOnlineAsync(string? subject, string? sessionId, CancellationToken ct = default);

    /// <summary>
    ///     Returns whether the online authority for the subject/session pair is live and carries
    ///     exactly the expected generation; anything less fails closed.
    /// </summary>
    Task<bool> ValidateOnlineAsync(string? subject, string? sessionId, string? generation, CancellationToken ct = default);

    Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default);
}