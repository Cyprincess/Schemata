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
    ///     Establishes (or rotates) the OP session for the given principal / subject and returns
    ///     the new session identifier, per
    ///     <seealso href="https://openid.net/specs/openid-connect-session-1_0.html#OPRequest">
    ///         OpenID Connect Session Management 1.0 §3.2: OP Request
    ///     </seealso>
    ///     . Returns the issued sid; when the host has no session backing it returns
    ///     <see langword="null" />.
    /// </summary>
    Task<string?> IssueAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default);
    Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default);
}