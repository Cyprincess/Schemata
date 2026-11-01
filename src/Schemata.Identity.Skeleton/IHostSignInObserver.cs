using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Identity.Skeleton;

/// <summary>
///     Observes host sign-in completion before the authentication ticket is serialized, letting a
///     bridge (for example the Authorization OP-session bridge) bind session identity to the
///     ticket the host is about to issue. Observers run for every successful sign-in; failures
///     propagate to the sign-in request.
/// </summary>
public interface IHostSignInObserver
{
    /// <summary>
    ///     Runs after credential validation succeeded and before the ticket is written. The prior
    ///     principal is the incoming <see cref="System.Security.Claims.ClaimsPrincipal" /> of the
    ///     sign-in request (already signed-out ticket), letting a bridge reuse same-subject state.
    /// </summary>
    /// <param name="principal">The principal about to be signed in.</param>
    /// <param name="prior">The incoming principal of the sign-in request, or null when anonymous.</param>
    /// <param name="ct">A cancellation token.</param>
    Task OnSigningInAsync(ClaimsPrincipal principal, ClaimsPrincipal? prior, CancellationToken ct);

    /// <summary>
    ///     Runs when a verified ticket is renewed: the fresh principal replaces the ticket's, and
    ///     observers carry over whatever ticket-bound state must survive the rebuild. No fresh
    ///     session is established here — renewal replays an issued session.
    /// </summary>
    /// <param name="fresh">The freshly rebuilt principal.</param>
    /// <param name="ticket">The verified principal from the renewed ticket.</param>
    /// <param name="ct">A cancellation token.</param>
    Task OnRenewingAsync(ClaimsPrincipal fresh, ClaimsPrincipal ticket, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    ///     Runs before the controller performs its final scheme sign-outs, letting a bridge end
    ///     the session authority it bound at sign-in and optionally supply the rendered response.
    ///     An observer may clear tickets it owns while doing that work. At most one observer may
    ///     return a response; conflicts fail the sign-out. Observer failures stop the controller's
    ///     remaining final sign-outs, while completed observer effects remain.
    /// </summary>
    /// <param name="principal">The principal being signed out.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The response to render, or null for the default no-content result.</returns>
    Task<HostSignOutResponse?> OnSigningOutAsync(ClaimsPrincipal principal, CancellationToken ct)
        => Task.FromResult<HostSignOutResponse?>(null);
}