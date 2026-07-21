using Schemata.Core.Features;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;
using Schemata.Resource.Grpc.Features;

namespace Schemata.Report.Grpc.Features;

/// <summary>Composes Report resource methods with the gRPC resource transport.</summary>
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataReportGrpcFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for Report gRPC endpoints.</summary>
    public const int DefaultPriority = SchemataReportFeature<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>.DefaultPriority + 200_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;
}
