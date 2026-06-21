using System;
using System.Threading.Tasks;
using Schemata.Common;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRuntimeSourceReferenceShould
{
    [Fact]
    public async Task StartProcess_WithSource_CapturesSourceReferenceColumns() {
        var fixture = new ProcessRuntimeFixture();
        var source = new FlowSourceEntity {
            CanonicalName = "orders/1", Timestamp = Identifiers.NewUid(),
        };

        await fixture.Runtime.StartProcessInstanceAsync("approval", sourceEntity: source);

        var row = Assert.Single(fixture.Persisted);
        Assert.Equal(typeof(FlowSourceEntity).FullName, row.SourceType);
        Assert.Equal("orders/1", row.Source);
        Assert.Equal(source.Timestamp, row.SourceTimestamp);
    }

    [Fact]
    public async Task StartProcess_WithoutSource_LeavesSourceColumnsNull() {
        var fixture = new ProcessRuntimeFixture();

        await fixture.Runtime.StartProcessInstanceAsync("approval");

        var row = Assert.Single(fixture.Persisted);
        Assert.Null(row.SourceType);
        Assert.Null(row.Source);
        Assert.Null(row.SourceTimestamp);
    }

    [Fact]
    public async Task StartProcess_SourceNotImplementingConcurrency_ThrowsInvalidOperationException() {
        var fixture = new ProcessRuntimeFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runtime
                                                                         .StartProcessInstanceAsync(
                                                                              "approval",
                                                                              sourceEntity: new ProcessRuntimeFixture.
                                                                                  NamedOnlySource())
                                                                         .AsTask());
    }
}
