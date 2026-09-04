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
/// <remarks>
///     Phase 0 adds invalidation; Phase 6 extends issuance as the single source of the
///     OP session identifier (<c>sid</c>).
/// </remarks>
public interface IOpSessionService
{
    Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default);
}