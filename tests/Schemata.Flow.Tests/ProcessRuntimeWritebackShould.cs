using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessRuntimeWritebackShould
{
    [Fact]
    public async Task WriteBackState_ToSourceEntity_OnTransition() {
        var fixture = new ProcessRuntimeFixture();
        var stamp = Identifiers.NewUid();
        var source = new FlowSourceEntity { CanonicalName = "orders/1", Timestamp = stamp };
        fixture.SourceRow = source;
        fixture.StartResult = new() { StateId = "draft", State = "Draft" };

        await fixture.Runtime.StartProcessInstanceAsync("approval", sourceEntity: source);

        Assert.Equal("Draft", source.State);
        Assert.Same(source, Assert.Single(fixture.SourceUpdates));
    }

    [Fact]
    public async Task Throw_FailedPrecondition_WhenSourceTimestampDrifts() {
        var fixture = new ProcessRuntimeFixture();
        var source = new FlowSourceEntity {
            CanonicalName = "orders/1", Timestamp = Identifiers.NewUid(),
        };
        fixture.SourceRow = new() { CanonicalName = "orders/1", Timestamp = Identifiers.NewUid() };

        await Assert.ThrowsAsync<FailedPreconditionException>(
            () => fixture.Runtime.StartProcessInstanceAsync("approval", sourceEntity: source).AsTask());

        Assert.Empty(fixture.SourceUpdates);
        Assert.Empty(fixture.Persisted);
    }

    [Fact]
    public async Task Skip_Writeback_WhenDisabled() {
        var fixture = new ProcessRuntimeFixture(sourceWriteback: false);
        var source = new FlowSourceEntity {
            CanonicalName = "orders/1", Timestamp = Identifiers.NewUid(),
        };
        fixture.SourceRow = source;

        await fixture.Runtime.StartProcessInstanceAsync("approval", sourceEntity: source);

        Assert.Null(source.State);
        Assert.Empty(fixture.SourceUpdates);
        Assert.Single(fixture.Persisted);
    }

    [Fact]
    public async Task Skip_Writeback_WhenSourceRowMissing() {
        var fixture = new ProcessRuntimeFixture();
        var source = new FlowSourceEntity {
            CanonicalName = "orders/1", Timestamp = Identifiers.NewUid(),
        };

        await fixture.Runtime.StartProcessInstanceAsync("approval", sourceEntity: source);

        Assert.Empty(fixture.SourceUpdates);
        Assert.Single(fixture.Persisted);
    }
}
