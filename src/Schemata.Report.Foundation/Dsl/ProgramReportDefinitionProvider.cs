using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton;
using Schemata.Report.Skeleton;

namespace Schemata.Report.Foundation;

internal sealed class ProgramReportDefinitionProvider(
    ReportDefinitionBuilder definition
) : IReportDefinitionProvider
{
    public ValueTask<QueryInsightRequest> GetDefinitionAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(definition.Build());
    }
}
