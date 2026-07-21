using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Report.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Report.Foundation;

internal static class ReportResourceRegistration<TReport, TSnapshot>
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
{
    internal static readonly ResourceMethodAttribute[] ReportMethods = [
        new(Verbs.Generate, typeof(GenerateHandler<TReport>), ResourceMethodScope.Collection),
    ];

    internal static readonly ResourceMethodAttribute[] SnapshotMethods = [
        new(Verbs.Read, typeof(ReadSnapshotHandler<TSnapshot>)) { Method = ResourceHttpMethod.Get },
    ];

    internal static readonly Operations[] SnapshotOperations = [Operations.List, Operations.Get];
}
