using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Admission policy for the <c>:run</c> custom method on <see cref="SchemataJob" />: callers
///     authorize against the <see cref="Verbs.Run" /> permission. Scheduler-initiated fires carry
///     no caller and enter the command lifecycle directly, so this policy only ever judges the
///     resource envelope.
/// </summary>
internal sealed class SchedulingAccessPolicy(IPermissionResolver permissions, IPermissionMatcher matcher) : IResourceTargetPolicy
{
    public Task<AccessDecision> EvaluateAsync(ResourceTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) {
            return Task.FromResult(AccessDecision.Denied);
        }

        var allowed = matcher.IsMatch(principal, permissions.Resolve(target.Operation, target.Entity!));
        return Task.FromResult(allowed ? AccessDecision.Allowed : AccessDecision.Denied);
    }
}
