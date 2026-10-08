using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Report.Foundation.Commands;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class ReportActorIdentityShould
{
    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("run")]
    [InlineData("scheduled")]
    [InlineData("sync")]
    public async Task Canonical_And_Leaf_Generations_Serialize_While_Another_Report_Remains_Independent(string entry) {
        var fixture = await ReportActorIdentityFixture.CreateAsync();
        await fixture.ExecuteAsync(async () => {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var first = GenerateCanonicalAsync(fixture, entry, timeout.Token);
            await fixture.Gate.First.WaitAsync(timeout.Token);
            var leafRequest = new ReportRequest { Name = "daily", Persist = true };
            var second = RunAsync(fixture, leafRequest, timeout.Token);
            var other = RunAsync(fixture, new() { Name = "reports/other", Persist = true }, timeout.Token);

            try {
                await fixture.Gate.Other.WaitAsync(timeout.Token);
                var independent = await other.WaitAsync(timeout.Token);
                Assert.NotNull(independent.Snapshot);
                Assert.False(first.IsCompleted);
                var observation = await Task.WhenAny(fixture.Gate.Overlap, Task.Delay(TimeSpan.FromSeconds(1), timeout.Token));
                Assert.NotSame(fixture.Gate.Overlap, observation);
            } finally {
                fixture.Gate.Release();
            }

            var canonical = await first.WaitAsync(timeout.Token);
            var leaf = await second.WaitAsync(timeout.Token);
            Assert.Equal(1, fixture.Gate.Maximum);
            Assert.Equal("daily", leafRequest.Name);
            Assert.NotEqual(canonical.Snapshot, leaf.Snapshot);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportActorIdentityDbContext>();
            var snapshots = await db.Set<SchemataReportSnapshot>().AsNoTracking()
                .Where(snapshot => snapshot.Report == "daily").ToListAsync(timeout.Token);
            Assert.Equal(new[] { canonical.Snapshot, leaf.Snapshot }.OrderBy(name => name),
                         snapshots.Select(snapshot => snapshot.CanonicalName).OrderBy(name => name));
            Assert.All(snapshots, snapshot => {
                Assert.Equal(SnapshotState.Succeeded, snapshot.State);
                Assert.Equal(1, snapshot.RowCount);
                Assert.Equal(1, snapshot.ChunkCount);
            });
            var store = scope.ServiceProvider.GetRequiredService<IReportSnapshotStore>();
            var values = new List<int>();
            foreach (var snapshot in snapshots) {
                await foreach (var row in store.ReadRowsAsync(snapshot.CanonicalName!, timeout.Token)) {
                    values.Add(((JsonElement)row["value"]!).GetInt32());
                }
            }
            Assert.Equal(new[] { 1, 2 }, values.OrderBy(value => value));

            var third = await RunAsync(fixture, new() { Name = "reports/daily", Persist = true }, timeout.Token);
            var retained = await db.Set<SchemataReportSnapshot>().AsNoTracking()
                .Where(snapshot => snapshot.Report == "daily").ToListAsync(timeout.Token);
            Assert.Equal(new[] { leaf.Snapshot, third.Snapshot }.OrderBy(name => name),
                         retained.Select(snapshot => snapshot.CanonicalName).OrderBy(name => name));
            Assert.Null(await store.GetAsync(canonical.Snapshot!, timeout.Token));
            Assert.Null(await store.GetChunkAsync(canonical.Snapshot!, 0, timeout.Token));
            Assert.Single(await db.Set<SchemataReportSnapshot>().AsNoTracking()
                .Where(snapshot => snapshot.Report == "other").ToListAsync(timeout.Token));
        });
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Synchronous_Generation_Preserves_Run_Command_Denial_Before_Snapshot_Writes() {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("report-consumer"));
        var denial = new Mock<IRequestPipelineAdvisor<RunReportRequest, ReportResult>>(MockBehavior.Strict);
        denial.SetupGet(advisor => advisor.Order).Returns(100);
        denial.Setup(advisor => advisor.AdviseAsync(
            It.IsAny<AdviceContext>(), It.Is<RunReportRequest>(request => request.Principal == principal),
            It.IsAny<RequestHandlerContinuation<ReportResult>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PermissionDeniedException());
        var fixture = await ReportActorIdentityFixture.CreateAsync(services => services.AddSingleton(denial.Object));
        await fixture.ExecuteAsync(async () => {
            await using var scope = fixture.Services.CreateAsyncScope();
            var operation = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                .SendAsync<GenerateReportRequest, Operation>(new() { Name = "reports/daily", Persist = true, Sync = true, Principal = principal });
            Assert.True(operation.Done);
            Assert.NotNull(operation.Error);
            Assert.Equal(2, operation.Error.Code);
            Assert.Null(operation.Response);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ReportActorIdentityDbContext>()
                .Set<SchemataReportSnapshot>().AsNoTracking().ToListAsync());
            var execution = await scope.ServiceProvider.GetRequiredService<ReportActorIdentityDbContext>()
                .Set<SchemataJobExecution>().AsNoTracking().SingleAsync(row => row.CanonicalName == operation.CanonicalName);
            Assert.Equal(ExecutionState.Failed, execution.State);
            denial.Verify(advisor => advisor.AdviseAsync(
                It.IsAny<AdviceContext>(), It.Is<RunReportRequest>(request => request.Principal == principal),
                It.IsAny<RequestHandlerContinuation<ReportResult>>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    private static async Task<ReportResult> RunAsync(
        ReportActorIdentityFixture fixture, ReportRequest request, CancellationToken ct) {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReportService>().RunAsync(request, ct: ct);
    }

    private static async Task<ReportResult> GenerateCanonicalAsync(
        ReportActorIdentityFixture fixture, string entry, CancellationToken ct) {
        await using var scope = fixture.Services.CreateAsyncScope();
        if (entry == "run") {
            return await scope.ServiceProvider.GetRequiredService<IReportService>()
                .RunAsync(new() { Name = "reports/daily", Persist = true }, ct: ct);
        }
        if (entry == "sync") {
            var request = new GenerateReportRequest { Name = "reports/daily", Persist = true, Sync = true };
            var operation = await scope.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                .SendAsync<GenerateReportRequest, Operation>(request, ct);
            Assert.True(operation.Done);
            Assert.Null(operation.Error);
            Assert.Equal("reports/daily", request.Name);
            return ReportResults.FromOperation(operation);
        }

        var execution = new SchemataJobExecution();
        var context = new JobContext {
            Variables = new Dictionary<string, string?> { ["report"] = "reports/daily" },
            Execution = execution,
        };
        await fixture.Services.GetRequiredService<ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>()
            .ExecuteAsync(context, ct);
        var output = JsonSerializer.Deserialize<ReportOperationOutput>(execution.Output!, SchemataJson.Default)!;
        return new() { Snapshot = output.Snapshot };
    }
}
