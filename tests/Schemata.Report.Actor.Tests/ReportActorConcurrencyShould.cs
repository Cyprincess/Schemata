using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Report.Actor.Tests.Fixtures;
using Schemata.Abstractions.Exceptions;
using Schemata.Report.Foundation.Commands;
using Schemata.Messaging.Skeleton;
using Schemata.Report.Foundation.Jobs;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Enums;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Xunit;

namespace Schemata.Report.Actor.Tests;

/// <summary>
///     Concurrency acceptance for the Report.Actor bridge (spec §7.2). N tasks fire the real
///     scheduled generation job (<see cref="ReportGenerationJob{TReport,TSnapshot,TChunk}" /> →
///     dispatcher → the wrapped <c>RunReportRequest</c> handler) at the same report name
///     simultaneously, each from its own DI scope. The report retains one snapshot, so the retention
///     "list all snapshots then trim" step races. With the bridge installed the per-name actor
///     serializes every generation, no concurrency conflict is raised, and retention converges
///     to exactly one succeeded snapshot; the control group without the bridge raises concurrency
///     conflicts or leaves excess succeeded snapshots behind, proving the harness manufactures
///     genuine contention.
/// </summary>
public class ReportActorConcurrencyShould
{
    private const int Concurrency = 16;

    [Fact]
    public async Task Concurrent_Scheduled_Generations_Of_Same_Report_Serialize_Without_Conflict_With_Actor() {
        await using var harness = await ReportActorConcurrencyHarness.BuildAsync(withActor: true);

        var outcome = await RunConcurrentGenerationsAsync(harness);

        Assert.Empty(outcome.Conflicts);
        var succeeded = await SucceededSnapshotsAsync(harness);
        Assert.Single(succeeded);
    }

    [Fact]
    public async Task Inline_Request_Bypasses_The_Actor_And_Still_Runs_With_Bridge_Installed() {
        await using var harness = await ReportActorConcurrencyHarness.BuildAsync(withActor: true);
        await using var scope   = harness.Root.CreateAsyncScope();

        var dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        var request    = new RunReportRequest(
            new ReportRequest {
                Persist = false,
                Query   = new() { Sources = { new("record", "source-records") } },
            },
            null);

        var result = await dispatcher.SendAsync<RunReportRequest, ReportResult>(request, CancellationToken.None);

        Assert.Equal(3, result.Response.Rows.Count);
    }

    [Fact]
    public async Task Concurrent_Scheduled_Generations_Without_Actor_Produce_Contention() {
        await using var harness = await ReportActorConcurrencyHarness.BuildAsync(withActor: false);

        var outcome    = await RunConcurrentGenerationsAsync(harness);
        var succeeded  = await SucceededSnapshotsAsync(harness);

        Assert.True(
            outcome.Conflicts.Count > 0 || succeeded.Count > 1,
            $"Control group raised {outcome.Conflicts.Count} concurrency conflicts and retained " +
            $"{succeeded.Count} succeeded snapshots: the harness is not manufacturing genuine retention " +
            "contention, so the actor-enabled case proves nothing.");
    }

    private static async Task<ConcurrencyOutcome> RunConcurrentGenerationsAsync(ReportActorConcurrencyHarness harness) {
        using var ready = new Barrier(Concurrency);
        var tasks = Enumerable.Range(0, Concurrency)
                              .Select(_ => Task.Run(async () => {
                                  var job = harness.Root.GetRequiredService<
                                      ReportGenerationJob<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>>();
                                  var context = new JobContext {
                                      Variables = new Dictionary<string, string?> { ["report"] = ReportActorConcurrencyHarness.ReportName },
                                  };
                                  ready.SignalAndWait();
                                  try {
                                      await job.ExecuteAsync(context, CancellationToken.None);
                                      return (Exception?)null;
                                  }
                                  catch (AbortedException ex) {
                                      return ex;
                                  }
                              }))
                              .ToArray();
        var results = await Task.WhenAll(tasks);
        return new(results.Where(result => result is not null).ToList()!);
    }

    private static async Task<List<SchemataReportSnapshot>> SucceededSnapshotsAsync(ReportActorConcurrencyHarness harness) {
        await using var scope   = harness.Root.CreateAsyncScope();
        var             factory = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        return await factory.Set<SchemataReportSnapshot>()
                            .Where(snapshot => snapshot.Report == ReportActorConcurrencyHarness.ReportName
                                            && snapshot.State == SnapshotState.Succeeded)
                            .ToListAsync();
    }
}