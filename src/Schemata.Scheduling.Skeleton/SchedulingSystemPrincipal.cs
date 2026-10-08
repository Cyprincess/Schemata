using System.Security.Claims;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Trusted identity for system-initiated durable triggers (back-channel logout, scheduled push,
///     and similar background producers with no authenticated caller). Producers set
///     <see cref="JobContext.Principal" /> to <see cref="Instance" /> explicitly; the scheduler
///     authorization policy admits only this exact instance, so an accidental
///     <see langword="null" /> principal is still rejected when security is activated.
/// </summary>
public static class SchedulingSystemPrincipal
{
    /// <summary>The shared trusted principal for system-initiated triggers.</summary>
    public static ClaimsPrincipal Instance { get; } = new(new ClaimsIdentity("Schemata.Scheduling.Infrastructure"));
}
