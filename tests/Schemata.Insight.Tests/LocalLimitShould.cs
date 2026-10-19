using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Insight.Foundation.Execution;
using Schemata.Insight.Skeleton.Plan;
using Xunit;

namespace Schemata.Insight.Tests;

[Trait("Layer", "Unit")]
public sealed class LocalLimitShould
{
    [Fact]
    public async Task Take_Stops_Reading_Upstream_After_The_Last_Requested_Row() {
        var probe    = new Probe(5);
        var executor = new LocalPipelineExecutor(new ServiceCollection().BuildServiceProvider());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in executor.RunStagesAsync(probe.Rows(), [Limit(0, 2)], CancellationToken.None)) {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task Skip_Counts_Toward_Upstream_But_Not_Toward_Take() {
        var probe    = new Probe(5);
        var executor = new LocalPipelineExecutor(new ServiceCollection().BuildServiceProvider());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in executor.RunStagesAsync(probe.Rows(), [Limit(2, 2)], CancellationToken.None)) {
            rows.Add(row);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, probe.Read);
        Assert.True(probe.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nonpositive_Take_Never_Opens_Upstream(int take) {
        var probe    = new Probe(5);
        var executor = new LocalPipelineExecutor(new ServiceCollection().BuildServiceProvider());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in executor.RunStagesAsync(probe.Rows(), [Limit(0, take)], CancellationToken.None)) {
            rows.Add(row);
        }

        Assert.Empty(rows);
        Assert.Equal(0, probe.Read);
    }

    [Fact]
    public async Task Null_Take_Enumerates_Upstream_To_The_End() {
        var probe    = new Probe(3);
        var executor = new LocalPipelineExecutor(new ServiceCollection().BuildServiceProvider());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in executor.RunStagesAsync(probe.Rows(), [Limit(null, null)], CancellationToken.None)) {
            rows.Add(row);
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(3, probe.Read);
        Assert.True(probe.Disposed);
    }

    private static LimitNode Limit(int? skip, int? take) {
        return new(new SourceNode("p", new("probe", new Dictionary<string, object?>())), skip, take);
    }

    private sealed class Probe(int count)
    {
        internal int  Read    { get; private set; }
        internal bool Disposed { get; private set; }

        internal async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(
            [EnumeratorCancellation] CancellationToken ct = default
        ) {
            try {
                for (var index = 0; index < count; index++) {
                    ct.ThrowIfCancellationRequested();
                    Read++;
                    yield return new Dictionary<string, object?> { ["value"] = index };
                    await Task.Yield();
                }
            } finally {
                Disposed = true;
            }
        }
    }
}
