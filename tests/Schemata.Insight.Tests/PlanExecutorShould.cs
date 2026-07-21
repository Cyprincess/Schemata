using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation;
using Schemata.Insight.Skeleton;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Insight.Tests;

public class PlanExecutorShould
{
    [Fact]
    public async Task Existing_ExecuteAsync_Behavior_Unchanged() {
        await using var services = CreateServices(new ProbeDriver(250));
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(Source(), 5, 250);
        var request  = new QueryInsightRequest { PageSize = 250, Skip = 99 };

        var response = await executor.ExecuteAsync(plan, request, null, CancellationToken.None);

        Assert.Equal(100, response.Rows.Count);
        Assert.Equal(5, response.Rows[0]["value"]);
        Assert.Equal(104, response.Rows[^1]["value"]);
        Assert.Equal(250, response.TotalSize);
        Assert.NotNull(response.NextPageToken);
    }

    [Fact]
    public async Task Materializes_All_Rows_Beyond_Page_Cap() {
        await using var services = CreateServices(new ProbeDriver(250));
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(Source(), 20, 1);
        var request  = new QueryInsightRequest { PageSize = 1, Skip = 200, PageToken = "not-a-page-token" };

        await using var materialized = await executor.MaterializeAsync(plan, request, null);

        Assert.Equal(250, await CountAsync(materialized.Rows));
    }

    [Fact]
    public async Task Strips_Only_Top_Level_Limit() {
        var driver = new ProbeDriver(1);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var plan     = new LimitNode(new LimitNode(Source(), 1, 3), 10, 20);

        await using var materialized = await executor.MaterializeAsync(plan, new(), null);

        var nested = Assert.IsType<LimitNode>(driver.LastSubPlan!.Root);
        Assert.Equal(1, nested.Skip);
        Assert.Equal(3, nested.Take);
        Assert.IsType<SourceNode>(nested.Input);
    }

    [Fact]
    public async Task Streams_Without_Full_Buffering() {
        var sourceCompleted = false;
        var driver          = new ProbeDriver(2, () => sourceCompleted = true);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await using var materialized = await executor.MaterializeAsync(Source(), new(), null);
        await using var rows = materialized.Rows.GetAsyncEnumerator();

        Assert.True(await rows.MoveNextAsync());
        Assert.False(sourceCompleted);
        Assert.True(await rows.MoveNextAsync());
        Assert.False(await rows.MoveNextAsync());
        Assert.True(sourceCompleted);
    }

    [Fact]
    public async Task Skips_Security_Gate_When_Disabled() {
        var access = new Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>>(MockBehavior.Strict);
        await using var services = CreateRepositoryDriverServices(access);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await using (var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false)) {
            await CountAsync(materialized.Rows);
        }

        var exception = await Assert.ThrowsAsync<TargetInvocationException>(async () => await executor.MaterializeAsync(RepositorySource(), new(), null));
        Assert.IsType<MockException>(exception.InnerException);
    }

    [Fact]
    public async Task Surfaces_Permission_Denied_When_Security_Enforced() {
        var access = new Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                                     It.IsAny<SchemataInsightSource?>(),
                                     It.IsAny<AccessContext<QueryInsightRequest>>(),
                                     It.IsAny<ClaimsPrincipal?>(),
                                     It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        await using var services = CreateRepositoryDriverServices(access);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => await executor.MaterializeAsync(RepositorySource(), new(), null));
    }

    [Fact]
    public async Task Disposes_Source_Result_After_Stream_Cancellation() {
        using var cts = new CancellationTokenSource();
        var driver = new ProbeDriver(2);
        await using var services = CreateServices(driver);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        var materialized = await executor.MaterializeAsync(Source(), new(), null, ct: cts.Token);

        try {
            await using var rows = materialized.Rows.GetAsyncEnumerator();
            Assert.True(await rows.MoveNextAsync());
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await rows.MoveNextAsync().AsTask());
        } finally {
            await materialized.DisposeAsync();
        }

        Assert.True(driver.LastResult!.Disposed);
    }

    [Fact]
    public async Task Creates_And_Disposes_Scoped_Repository_Dependency_Per_Execution() {
        var tracker = new ScopeTracker();
        await using var services = CreateScopeValidatingRepositoryDriverServices(tracker);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));

        var response = await executor.ExecuteAsync(RepositorySource(), new(), null, CancellationToken.None);

        Assert.Single(response.Rows);
        Assert.Equal(1, tracker.Created);
        Assert.Equal(1, tracker.Disposed);

        await using (var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false)) {
            Assert.Equal(1, await CountAsync(materialized.Rows));
            Assert.Equal(2, tracker.Created);
            Assert.Equal(1, tracker.Disposed);
        }

        Assert.Equal(2, tracker.Disposed);
    }

    [Fact]
    public async Task Disposes_Scoped_Repository_Dependency_After_Cancelled_Materialized_Stream() {
        var tracker = new ScopeTracker();
        await using var services = CreateScopeValidatingRepositoryDriverServices(tracker, 2);
        var executor = new PlanExecutor(services, new LocalPipelineExecutor(services), Options.Create(new SchemataInsightOptions()));
        using var cts = new CancellationTokenSource();
        var materialized = await executor.MaterializeAsync(RepositorySource(), new(), null, enforceSecurity: false, ct: cts.Token);

        try {
            await using var rows = materialized.Rows.GetAsyncEnumerator();
            Assert.True(await rows.MoveNextAsync());
            cts.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await rows.MoveNextAsync().AsTask());
        } finally {
            await materialized.DisposeAsync();
        }

        Assert.Equal(1, tracker.Created);
        Assert.Equal(1, tracker.Disposed);
    }

    private static ServiceProvider CreateServices(ISourceDriver driver) {
        return new ServiceCollection()
              .AddKeyedSingleton<ISourceDriver>(ProbeDriver.DriverName, driver)
              .BuildServiceProvider();
    }

    private static SourceNode Source() {
        return new("p", new(ProbeDriver.DriverName, new Dictionary<string, object?>()));
    }

    private static SourceNode RepositorySource() {
        return new("r", new(RepositoryDriver.DriverName, new Dictionary<string, object?> { ["resource"] = "insightSources" }));
    }

    private static ServiceProvider CreateRepositoryDriverServices(
        Mock<IAccessProvider<SchemataInsightSource, QueryInsightRequest>> access
    ) {
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository.Setup(r => r.ListAsync<SchemataInsightSource>(
                              It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> query,
                            CancellationToken                                                         ct)
                           => EntityRows(query(new[] { new SchemataInsightSource { Name = "source" } }.AsQueryable()), ct));

        return new ServiceCollection()
              .AddSingleton(repository.Object)
              .AddSingleton(access.Object)
              .AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName, static (services, _) => new RepositoryDriver(services))
               .BuildServiceProvider();
    }

    private static ServiceProvider CreateScopeValidatingRepositoryDriverServices(ScopeTracker tracker, int rowCount = 1) {
        var repository = new Mock<IRepository<SchemataInsightSource>>();
        repository.Setup(r => r.ListAsync<SchemataInsightSource>(
                              It.IsAny<Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataInsightSource>, IQueryable<SchemataInsightSource>> query,
                            CancellationToken                                                         ct)
                           => EntityRows(
                               query(Enumerable.Range(0, rowCount)
                                               .Select(value => new SchemataInsightSource { Name = $"source-{value}" })
                                               .AsQueryable()),
                               ct));

        return new ServiceCollection()
              .AddScoped(_ => new ScopeProbe(tracker))
              .AddTransient<IRepository<SchemataInsightSource>>(services => {
                  _ = services.GetRequiredService<ScopeProbe>();
                  return repository.Object;
              })
              .AddKeyedSingleton<ISourceDriver>(RepositoryDriver.DriverName, static (services, _) => new RepositoryDriver(services))
              .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async IAsyncEnumerable<SchemataInsightSource> EntityRows(
        IEnumerable<SchemataInsightSource> rows,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        foreach (var row in rows) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private static async Task<int> CountAsync(IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows) {
        var count = 0;
        await foreach (var _ in rows) {
            count++;
        }

        return count;
    }

    private sealed class ProbeDriver(int count, Action? completed = null) : ISourceDriver
    {
        public const string DriverName = "probe";

        public string Name => DriverName;

        public DriverCapabilities Capabilities => DriverCapabilities.None;

        public SubPlan? LastSubPlan { get; private set; }

        public ProbeResult? LastResult { get; private set; }

        public ValueTask<ISourceResult> ExecuteAsync(
            SubPlan             subPlan,
            QueryInsightRequest request,
            ClaimsPrincipal?    principal,
            CancellationToken   ct = default
        ) {
            LastSubPlan = subPlan;
            LastResult  = new(Rows(count, completed, ct), [new("value", FieldType.Int64, null, false, [])]);
            return new(LastResult);
        }

        private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows(
            int count,
            Action? completed,
            [EnumeratorCancellation] CancellationToken ct = default
        ) {
            for (var value = 0; value < count; value++) {
                ct.ThrowIfCancellationRequested();
                yield return new Dictionary<string, object?> { ["value"] = value };
                await Task.Yield();
            }

            completed?.Invoke();
        }
    }

    private sealed class ProbeResult(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<FieldDescriptor>                        schema
    ) : ISourceResult
    {
        public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Rows { get; } = rows;

        public IReadOnlyList<FieldDescriptor> Schema { get; } = schema;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync() {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopeTracker
    {
        public int Created { get; set; }

        public int Disposed { get; set; }
    }

    private sealed class ScopeProbe : IDisposable
    {
        private readonly ScopeTracker _tracker;

        public ScopeProbe(ScopeTracker tracker) {
            _tracker = tracker;
            _tracker.Created++;
        }

        public void Dispose() {
            _tracker.Disposed++;
        }
    }
}
