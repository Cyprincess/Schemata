using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Common;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Foundation.Snapshots;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Grpc;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public sealed class ReportRunningSchemaShould
{
    [Fact]
    public async Task Publish_Typed_Schema_Before_A_Running_Chunk_Becomes_Readable() {
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var source = new Mock<ISourceResult>();
        source.SetupGet(result => result.Schema).Returns(new FieldDescriptor[] {
            new("number", FieldType.Object, "value", false, []),
            new("precise", FieldType.Decimal, "value", false, []),
            new("unsigned", FieldType.UInt64, "value", false, []),
            new("missing", FieldType.Decimal, "value", false, []),
        });
        source.SetupGet(result => result.Rows).Returns(Rows(cancellation.Token));
        source.Setup(result => result.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = new Mock<ISourceDriver>();
        driver.SetupGet(value => value.Name).Returns("running-schema");
        driver.SetupGet(value => value.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(value => value.ExecuteAsync(It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(),
                It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source.Object);
        await using var factory = new WebAppFactory(services => {
            services.AddKeyedSingleton<ISourceDriver>("running-schema", driver.Object);
            services.Configure<SchemataInsightOptions>(options => options.Sources["running-schema"] = new("running-schema", new Dictionary<string, object?>()));
            services.Configure<SchemataReportOptions>(options => options.ChunkSize = 1);
        });
        using var scope = factory.Services.CreateScope();
        var run = scope.ServiceProvider.GetRequiredService<IReportService>().RunAsync(new() {
            Persist = true, Query = new() { Sources = [new("value", "running-schema")] },
        }, ct: cancellation.Token).AsTask();
        try {
            await holding.Task.WaitAsync(cancellation.Token);
            string snapshotName;
            using (var readerScope = factory.Services.CreateScope()) {
                var store = readerScope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
                var headers = new List<SchemataReportSnapshot>();
                await foreach (var header in store.ListAsync("reports/inline", cancellation.Token)) headers.Add(header);
                var running = Assert.Single(headers);
                Assert.Equal(SnapshotState.Running, running.State);
                snapshotName = running.CanonicalName!;
                Assert.NotNull(await store.GetChunkAsync(snapshotName, 0, cancellation.Token));
            }
            var runningPage = await Read(factory, snapshotName, cancellation.Token);
            AssertPage(runningPage);
            Assert.False(run.IsCompleted);
            release.TrySetResult();
            var result = await run;
            Assert.Equal(snapshotName, result.Snapshot);
            var completedPage = await Read(factory, snapshotName, cancellation.Token);
            AssertPage(completedPage);
        } finally {
            release.TrySetResult();
            await run;
        }
        source.Verify(result => result.DisposeAsync(), Times.Once);

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows([EnumeratorCancellation] CancellationToken ct) {
            yield return new Dictionary<string, object?> {
                ["number"] = 1.25d, ["precise"] = 7922816251426433759354395033.5m,
                ["unsigned"] = ulong.MaxValue, ["missing"] = null,
            };
            holding.TrySetResult();
            await release.Task.WaitAsync(ct);
        }
    }

    [Fact]
    public async Task Refresh_Running_Schema_After_Chunks_Published_During_The_Read() {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var firstChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldHeader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Mock<ISourceResult>();
        source.SetupGet(result => result.Schema).Returns(new[] { new FieldDescriptor("number", FieldType.Object, "value", false, []) });
        source.SetupGet(result => result.Rows).Returns(Rows(cancellation.Token));
        source.Setup(result => result.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var driver = new Mock<ISourceDriver>();
        driver.SetupGet(value => value.Name).Returns("schema-race");
        driver.SetupGet(value => value.Capabilities).Returns(DriverCapabilities.None);
        driver.Setup(value => value.ExecuteAsync(It.IsAny<SubPlan>(), It.IsAny<QueryInsightRequest>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source.Object);
        var selected = new Mock<IReportSnapshotStore>(MockBehavior.Strict);
        DefaultReportSnapshotStore<SchemataReportSnapshot, SchemataReportSnapshotChunk>? persisted = null;
        var reads = 0;
        selected.Setup(store => store.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string name, CancellationToken ct) => {
                var header = await persisted!.GetAsync(name, ct);
                if (Interlocked.Increment(ref reads) == 1) {
                    Assert.Equal(FieldType.Object, Assert.Single(System.Text.Json.JsonSerializer.Deserialize<FieldDescriptor[]>(header!.Schema!, SchemataJson.Default)!).Type);
                    oldHeader.TrySetResult();
                    await resumeReader.Task.WaitAsync(ct);
                }
                return header;
            });
        selected.Setup(store => store.GetChunkAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string name, int index, CancellationToken ct) => persisted!.GetChunkAsync(name, index, ct));
        await using var factory = new WebAppFactory(services => {
            services.AddKeyedSingleton<ISourceDriver>("schema-race", driver.Object);
            services.Configure<SchemataInsightOptions>(options => options.Sources["schema-race"] = new("schema-race", new Dictionary<string, object?>()));
            services.Configure<SchemataReportOptions>(options => options.ChunkSize = 1);
            services.AddKeyedSingleton<IReportSnapshotStore>(ReportConstants.Services.Selected, selected.Object);
        });
        persisted = new(factory.Services.GetRequiredService<IServiceScopeFactory>());
        using var scope = factory.Services.CreateScope();
        var run = scope.ServiceProvider.GetRequiredService<IReportService>().RunAsync(new() {
            Persist = true, Query = new() { Sources = [new("value", "schema-race")] },
        }, ct: cancellation.Token).AsTask();
        try {
            await firstChunk.Task.WaitAsync(cancellation.Token);
            var headers = new List<SchemataReportSnapshot>();
            await foreach (var header in persisted.ListAsync("reports/inline", cancellation.Token)) headers.Add(header);
            var snapshot = Assert.Single(headers).CanonicalName!;
            var read = Read(factory, snapshot, cancellation.Token, 2);
            await oldHeader.Task.WaitAsync(cancellation.Token);
            publishSecond.TrySetResult();
            await secondChunk.Task.WaitAsync(cancellation.Token);
            Assert.False(run.IsCompleted);
            resumeReader.TrySetResult();
            var page = await read;
            Assert.Equal(2, page.Rows.Count);
            Assert.Equal(FieldType.Double, Assert.Single(page.Schema).Type);
            Assert.True(page.Rows[0].Fields["number"].NullValue);
            Assert.Equal(1.25d, page.Rows[1].Fields["number"].NumberValue);
            Assert.Null(page.Rows[1].Fields["number"].StringValue);
        } finally {
            publishSecond.TrySetResult();
            resumeReader.TrySetResult();
            finish.TrySetResult();
            await run;
        }

        async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows([EnumeratorCancellation] CancellationToken ct) {
            yield return new Dictionary<string, object?> { ["number"] = null };
            firstChunk.TrySetResult();
            await publishSecond.Task.WaitAsync(ct);
            yield return new Dictionary<string, object?> { ["number"] = 1.25d };
            secondChunk.TrySetResult();
            await finish.Task.WaitAsync(ct);
        }
    }

    private static void AssertPage(ReadSnapshotGrpcResponse page) {
        var row = Assert.Single(page.Rows).Fields;
        Assert.Equal(FieldType.Double, page.Schema.Single(field => field.Name == "number").Type);
        Assert.Equal(1.25d, row["number"].NumberValue);
        Assert.Null(row["number"].StringValue);
        Assert.Equal("7922816251426433759354395033.5", row["precise"].StringValue);
        Assert.Equal("18446744073709551615", row["unsigned"].StringValue);
        Assert.True(row["missing"].NullValue);
    }

    private static async Task<ReadSnapshotGrpcResponse> Read(WebAppFactory factory, string snapshot, CancellationToken ct, int pageSize = 1) {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ReadSnapshotRequest));
        model.Add(typeof(ReadSnapshotGrpcResponse), true);
        var descriptor = ResourceNameDescriptor.ForType<SchemataReportSnapshot>();
        var method = new Method<ReadSnapshotRequest, ReadSnapshotGrpcResponse>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataReportSnapshot)),
            GrpcResourceNaming.CustomMethodName(descriptor, "read"),
            GrpcMarshallers.Create<ReadSnapshotRequest>(model), GrpcMarshallers.Create<ReadSnapshotGrpcResponse>(model));
        using var channel = factory.CreateGrpcChannel();
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(cancellationToken: ct),
            new() { CanonicalName = snapshot, PageSize = pageSize });
        return await call.ResponseAsync;
    }
}
