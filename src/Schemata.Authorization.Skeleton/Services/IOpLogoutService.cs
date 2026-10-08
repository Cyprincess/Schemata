using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     The single logout orchestration path, per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">
///         OpenID Connect RP-Initiated Logout 1.0
///     </seealso>
///     . Both the RP-initiated end-session endpoint and the host sign-out lifecycle run through
///     this service: notifiers snapshot their relying-party recipients, the OP session and its
///     host/browser adapters are invalidated, prepared notifications are dispatched, and the
///     targeted participation facts are retired. Invalidation failure closes the logout:
///     dispatch and retirement never run for a session that could not be terminated.
/// </summary>
public interface IOpLogoutService
{
    /// <summary>
    ///     Logs the subject/session pair out and returns the prepared front-channel logout URIs
    ///     the caller may render. When no session identifier is supplied, one canonical target
    ///     is resolved from session evidence before notifier preparation, and that target drives
    ///     invalidation and retirement. Offline grant families are not session state and stay
    ///     active.
    /// </summary>
    /// <param name="principal">The authenticated host principal, when a host initiated the logout.</param>
    /// <param name="subject">The canonical subject to log out.</param>
    /// <param name="sessionId">The OP session identifier to log out, when known.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<OpLogoutResult> LogoutAsync(
        ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default);
}