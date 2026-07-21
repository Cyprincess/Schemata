using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Insight.Skeleton;
using Schemata.Report.Foundation;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class ReportServiceIntegrationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ReportServiceIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Run_Dsl_Inline_And_Through_Operation() {
        using var scope = _factory.Services.CreateScope();
        var reports    = scope.ServiceProvider.GetRequiredService<IReportService>();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationService>();
        var snapshots  = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();

        var inline = await reports.RunAsync(new() { Name = "dsl-records" });
        Assert.Equal([1, 2, 3], Values(inline));
        Assert.Null(inline.Snapshot);

        var pending = await reports.GenerateAsync(new() { Name = "dsl-records", Persist = true });
        Assert.False(pending.Done);

        var completed = await WaitForDoneAsync(operations, pending.CanonicalName!);
        var result    = ReportResults.FromOperation(completed);
        Assert.NotNull(result.Snapshot);
        Assert.NotNull(await snapshots.GetAsync(result.Snapshot!));
    }

    [Fact]
    public async Task Trigger_Periodic_Report_Twice_Creates_Fresh_Snapshots() {
        using var scope = _factory.Services.CreateScope();
        var scheduler   = scope.ServiceProvider.GetRequiredService<IScheduler>();
        var operations  = scope.ServiceProvider.GetRequiredService<IOperationService>();
        var snapshots   = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
        var executions  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        var armed = await executions.FirstOrDefaultAsync(
                        query => query.Where(execution => execution.Job == "jobs/report-periodic-records"
                                                       && execution.State == ExecutionState.Pending));
        Assert.NotNull(armed);

        var before = await ListSnapshotsAsync(snapshots, "periodic-records");
        var first  = await TriggerAsync(scheduler, operations);
        var second = await TriggerAsync(scheduler, operations);

        Assert.NotEqual(first, second);
        var after = await ListSnapshotsAsync(snapshots, "periodic-records");
        Assert.True(after.Count >= before.Count + 2,
                    $"Expected at least {before.Count + 2} snapshots after two triggers, found {after.Count}.");
        Assert.Contains(after, snapshot => snapshot.CanonicalName == first);
        Assert.Contains(after, snapshot => snapshot.CanonicalName == second);
    }

    [Fact]
    public async Task Enforce_Insight_Source_Security_Through_Access_Providers() {
        using var factory = new WebAppFactory(services =>
            services.AddScoped<IAccessProvider<SourceRecord, QueryInsightRequest>, DenySourceRecordAccess>());
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();

        await Assert.ThrowsAsync<PermissionDeniedException>(async () =>
            await service.RunAsync(new() { Name = "dsl-records" }));
    }

    [Fact]
    public async Task Generate_Without_Scheduling_Returns_A_Clear_Error_For_Sync_And_Async_Http() {
        using var factory = new WebAppFactory("WithoutScheduling");
        using var client  = factory.CreateClient();
        const string request = """
            { "query": { "sources": [{ "alias": "record", "name": "source-records" }] }, "persist": false
            """;

        var sync = await client.PostAsync(
                       "/v1/reports:generate",
                       new StringContent(request + ", \"sync\": true }", Encoding.UTF8, "application/json"));
        var asynchronous = await client.PostAsync(
                              "/v1/reports:generate",
                              new StringContent(request + " }", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, sync.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, asynchronous.StatusCode);
        Assert.Contains("operation service", await sync.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operation service", await asynchronous.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> TriggerAsync(IScheduler scheduler, IOperationService operations) {
        var execution = await scheduler.TriggerAsync<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>(
                            new() {
                                Method = "generate",
                                Variables = new Dictionary<string, string?> {
                                    ["report"] = "periodic-records",
                                },
                            },
                            CancellationToken.None);
        var result = ReportResults.FromOperation(await WaitForDoneAsync(operations, execution.CanonicalName!));
        return result.Snapshot!;
    }

    private static async Task<List<SchemataReportSnapshot>> ListSnapshotsAsync(
        IReportSnapshotStore snapshots,
        string               report
    ) {
        var result = new List<SchemataReportSnapshot>();
        await foreach (var snapshot in snapshots.ListAsync(report)) {
            result.Add(snapshot);
        }

        return result;
    }

    private static async Task<Schemata.Abstractions.Resource.Operation> WaitForDoneAsync(
        IOperationService operations,
        string            operation
    ) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await operations.WaitAsync(operation, timeout.Token);
    }

    private static List<int> Values(ReportResult result) {
        return result.Response.Rows.Select(row => Convert.ToInt32(row["value"])).ToList();
    }
}
