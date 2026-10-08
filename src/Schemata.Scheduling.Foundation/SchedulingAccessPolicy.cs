using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Admission policy for the durable trigger envelope. The internal
///     <see cref="SchedulingOperations.Trigger" /> verb authorizes against the public
///     <see cref="Verbs.Run" /> permission; <see cref="SchedulingSystemPrincipal.Instance" />
///     is admitted as the trusted identity for system-initiated fires.
/// </summary>
internal sealed class SchedulingAccessPolicy(IPermissionResolver permissions, IPermissionMatcher matcher) : IResourceTargetPolicy
{
    public Task<AccessDecision> EvaluateAsync(ResourceTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) {
            return Task.FromResult(AccessDecision.Denied);
        }

        if (ReferenceEquals(principal, SchedulingSystemPrincipal.Instance)) {
            return Task.FromResult(AccessDecision.Allowed);
        }

        var operation = target.Operation == SchedulingOperations.Trigger ? Verbs.Run : target.Operation;
        var allowed   = matcher.IsMatch(principal, permissions.Resolve(operation, target.Entity!));
        return Task.FromResult(allowed ? AccessDecision.Allowed : AccessDecision.Denied);
    }
}
