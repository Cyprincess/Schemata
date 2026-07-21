using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Common;
using Schemata.Report.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;
using Xunit;

namespace Schemata.Report.Tests;

public class SchemataReportSnapshotChunkShould
{
    [Fact]
    public void Descriptor_Maps_Leaf_Identifiers_For_Nested_Chunk() {
        var descriptor = ResourceNameDescriptor.ForType(typeof(SchemataReportSnapshotChunk));

        Assert.Equal("reports/{report}/snapshots/{snapshot}/chunks", descriptor.CollectionPath);
        Assert.Equal("Chunk", descriptor.Singular);
        Assert.Equal(
            "reports/daily-sales/snapshots/2026-07-18/chunks/chunk-0",
            descriptor.Resolve(new SchemataReportSnapshotChunk {
                Report   = "daily-sales",
                Snapshot = "2026-07-18",
                Name     = "chunk-0",
            }));
    }

    [Fact]
    public void Chunk_Is_Not_Auto_Registered_As_Resource() {
        var services = new ServiceCollection();
        services.Configure<SchemataResourceOptions>(_ => { });

        SchemataResourceFeature.RegisterDiscoveredResources(
            services,
            typeof(SchemataReportSnapshotChunk).Assembly.GetExportedTypes());

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SchemataResourceOptions>>().Value;

        Assert.False(options.Resources.ContainsKey(typeof(SchemataReportSnapshotChunk).TypeHandle));
    }
}
