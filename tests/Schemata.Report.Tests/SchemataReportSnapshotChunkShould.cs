using Schemata.Common;
using Xunit;
using Schemata.Report.Skeleton.Entities;

namespace Schemata.Report.Tests;

public class SchemataReportSnapshotChunkShould
{
    [Fact]
    public void Descriptor_Maps_Leaf_Identifiers_For_Nested_Chunk() {
        var descriptor = ResourceNameDescriptor.ForType(typeof(SchemataReportSnapshotChunk));

        Assert.Equal("reports/{report}/snapshots/{snapshot}/chunks", descriptor.CollectionPath);
        Assert.Equal("Chunk", descriptor.Singular);
        Assert.Equal("Chunks", descriptor.Plural);
        Assert.Equal("chunks", descriptor.Collection);
        Assert.Equal(
            "reports/daily-sales/snapshots/2026-07-18/chunks/chunk-0",
            descriptor.Resolve(new SchemataReportSnapshotChunk {
                Report   = "daily-sales",
                Snapshot = "2026-07-18",
                Name     = "chunk-0",
            }));
    }
}
