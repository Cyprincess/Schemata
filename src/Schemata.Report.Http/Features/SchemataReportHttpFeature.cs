using Schemata.Core.Features;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;
using Schemata.Resource.Http.Features;

namespace Schemata.Report.Http.Features;

/// <summary>Composes Report resource methods with the HTTP resource transport.</summary>
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataReportHttpFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for Report HTTP endpoints.</summary>
    public const int DefaultPriority = SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>.DefaultPriority + 100_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;
}
