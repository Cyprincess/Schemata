using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation.Definitions;

internal interface IReportDefinitionSource
{
    ValueTask<(SchemataReport Report, QueryInsightRequest Query)?> ResolveAsync(string name, CancellationToken ct);

    IAsyncEnumerable<SchemataReport> ListPeriodicAsync(CancellationToken ct);
}
