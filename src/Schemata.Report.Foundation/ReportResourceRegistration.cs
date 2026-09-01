using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;
using Schemata.Report.Foundation.Handlers;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Foundation;

internal static class ReportResourceRegistration<TReport, TSnapshot, TChunk>
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    internal static readonly ResourceMethodAttribute[] ReportMethods = [
        new(Verbs.Generate, typeof(GenerateHandler<TReport, TSnapshot, TChunk>), ResourceMethodScope.Collection),
    ];

    internal static readonly ResourceMethodAttribute[] SnapshotMethods = [
        new(Verbs.Read, typeof(ReadSnapshotHandler<TSnapshot>)) { Method = ResourceHttpMethod.Get },
    ];

    internal static readonly Operations[] SnapshotOperations = [Operations.List, Operations.Get];
}
