using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton;

namespace Schemata.Flow.Foundation;

public sealed class FlowSubjectResolver : IFlowSubjectResolver
{
    public ValueTask<FlowSubjects> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default) {
        foreach (var identity in principal.Identities) {
            if (!identity.IsAuthenticated) continue;
            var subject = identity.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(subject)) return new(new FlowSubjects(subject, []));
        }
        return new(new FlowSubjects(null, []));
    }
}
