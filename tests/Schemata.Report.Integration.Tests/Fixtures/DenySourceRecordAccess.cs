using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;
using Schemata.Security.Skeleton;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class DenySourceRecordAccess : IAccessProvider<SourceRecord, QueryInsightRequest>
{
    public Task<bool> HasAccessAsync(
        SourceRecord?                entity,
        AccessContext<QueryInsightRequest> context,
        ClaimsPrincipal?             principal,
        CancellationToken            ct = default
    ) {
        return Task.FromResult(false);
    }
}
