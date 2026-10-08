using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Report.Integration.Tests.Fixtures;
using Schemata.Report.Skeleton.Enums;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using System.Threading;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Scheduling.Foundation.Commands;

namespace Schemata.Report.Integration.Tests;

[Trait("Category", "Integration")]
public class ReportScheduleLifecycleIntegrationShould
{
    [Fact]
    [Trait("Layer", "Integration")]
    public async Task Captured_Active_Recovery_Cannot_Overwrite_A_Committed_Pause() {
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var barrier = new RecoveryBarrier(captured, release);
        await using var host = await ReportScheduleLifecycleHost.CreateAsync(configure: services =>
            services.AddSingleton<IRequestPipelineAdvisor<RescheduleJobRequest, Unit>>(barrier));
        var report = NewReport("recovery-race");
        await CreateAsync(host, report);
        var active = await AssertActiveAsync(host, report, report.IntervalTicks!.Value);
        barrier.Target = active.CanonicalName;
        var recovery = host.Services.GetRequiredService<IScheduler>().RescheduleAsync(active, null, CancellationToken.None);
        try {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await host.Services.GetRequiredService<IScheduler>().UnscheduleAsync(active.CanonicalName!, CancellationToken.None);
            var paused = await AssertPausedAsync(host, report);
            Assert.NotEqual(active.ScheduleVersion, paused.ScheduleVersion);
            release.SetResult();
            await recovery;
            var unchanged = await AssertPausedAsync(host, report);
            Assert.Equal(paused.ScheduleVersion, unchanged.ScheduleVersion);
            Assert.Equal(paused.Timestamp, unchanged.Timestamp);
        } finally {
            release.TrySetResult();
            await recovery;
        }
    }

    private sealed class RecoveryBarrier(TaskCompletionSource captured, TaskCompletionSource release)
        : IRequestPipelineAdvisor<RescheduleJobRequest, Unit>
    {
        public string? Target { get; set; }
        public int Order => 0;
        public async Task<Unit> AdviseAsync(AdviceContext context, RescheduleJobRequest request,
            RequestHandlerContinuation<Unit> next, CancellationToken ct) {
            if (Target is not null && request.JobCanonicalName == Target) {
                captured.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            return await next(ct);
        }
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Create_After_Host_Startup_Arms_One_Canonical_Slot_With_Consumer_Job_Name() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        Assert.IsType<DefaultScheduler>(host.Services.GetRequiredService<IScheduler>());
        var report = NewReport("created");

        Assert.Equal(MutationResult.Applied, await CreateAsync(host, report));

        var job = await AssertActiveAsync(host, report, TimeSpan.FromDays(1).Ticks);
        Assert.StartsWith("consumer-", job.Name);
        Assert.Equal($"jobs/{job.Name}", job.CanonicalName);
        Assert.Equal(report.CanonicalName, job.Variables!["report"]);
        Assert.Equal(report.CanonicalName, (await ReportsAsync(host)).Single().CanonicalName);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Period_Change_Replaces_Pending_Execution_And_Preserves_Job_Identity() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("changed");
        await CreateAsync(host, report);
        var before = await AssertActiveAsync(host, report, report.IntervalTicks!.Value);
        var original = (await ExecutionsAsync(host)).Single(execution => execution.State == ExecutionState.Pending);
        report.IntervalTicks = TimeSpan.FromDays(2).Ticks;

        Assert.Equal(MutationResult.Applied, await UpdateAsync(host, report));

        var after = await AssertActiveAsync(host, report, report.IntervalTicks.Value);
        Assert.Equal(before.Uid, after.Uid);
        Assert.Equal(before.CanonicalName, after.CanonicalName);
        Assert.NotEqual(before.ScheduleVersion, after.ScheduleVersion);
        Assert.Contains(await ExecutionsAsync(host), execution => execution.Uid == original.Uid && execution.State == ExecutionState.Cancelled);
        Assert.Equal(report.IntervalTicks, (await ReportsAsync(host)).Single().IntervalTicks);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Create_Nonperiodic_Definition_Leaves_No_Job() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("manual");
        report.Periodic = false;
        Assert.Equal(MutationResult.Applied, await CreateAsync(host, report));

        await host.InitializeAsync();

        Assert.Empty(await JobsAsync(host));
        Assert.Empty(await ExecutionsAsync(host));
        Assert.False((await ReportsAsync(host)).Single().Periodic);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Undelete_Inactive_Or_Nonperiodic_Definition_Keeps_The_Job_Disarmed(bool periodic, bool deleted) {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("inactive");
        await CreateAsync(host, report);
        await DeleteAsync(host, report);
        report.Periodic = periodic;
        report.DeleteTime = deleted ? report.DeleteTime : null;

        Assert.Equal(MutationResult.Applied, await UpdateAsync(host, report, Operations.Undelete));
        await host.InitializeAsync();

        await AssertPausedAsync(host, report);
        Assert.Equal(deleted, (await ReportsAsync(host)).Single().DeleteTime.HasValue);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Update_To_Nonperiodic_Disarms_The_Existing_Slot() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("disabled");
        await CreateAsync(host, report);
        report.Periodic = false;

        Assert.Equal(MutationResult.Applied, await UpdateAsync(host, report));

        await AssertPausedAsync(host, report);
        Assert.False((await ReportsAsync(host)).Single().Periodic);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Soft_Delete_Disarms_And_Undelete_Rearms_The_Same_Job() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("restored");
        await CreateAsync(host, report);
        var original = await AssertActiveAsync(host, report, report.IntervalTicks!.Value);

        Assert.Equal(MutationResult.Applied, await DeleteAsync(host, report));
        Assert.NotNull((await ReportsAsync(host)).Single().DeleteTime);
        await AssertPausedAsync(host, report);
        await host.InitializeAsync();
        await AssertPausedAsync(host, report);

        report.DeleteTime = null;
        report.PurgeTime = null;
        Assert.Equal(MutationResult.Applied, await UpdateAsync(host, report, Operations.Undelete));

        var restored = await AssertActiveAsync(host, report, report.IntervalTicks.Value);
        Assert.Equal(original.Uid, restored.Uid);
        Assert.Null((await ReportsAsync(host)).Single().DeleteTime);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(Operations.Expunge)]
    [InlineData(Operations.Purge)]
    public async Task Physical_Delete_Disarms_And_Removes_The_Definition(Operations operation) {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("physical");
        await CreateAsync(host, report);

        Assert.Equal(MutationResult.Applied, await DeleteAsync(host, report, operation));

        Assert.Empty(await ReportsAsync(host));
        await AssertPausedAsync(host, report);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Repeated_Registration_And_Initializer_Keep_One_Active_Job_Per_Target() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync(configured: true);
        var report = NewReport("persisted");
        await CreateAsync(host, report);
        var before = (await JobsAsync(host)).ToDictionary(job => job.Key!, job => job.Uid);

        await host.InitializeAsync();
        await host.InitializeAsync();

        var jobs = await JobsAsync(host);
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job => {
            Assert.Equal(JobState.Active, job.State);
            Assert.Equal(before[job.Key!], job.Uid);
        });
        Assert.Contains(jobs, job => job.Key == "report:configured" && job.Variables!["report"] == "configured");
        Assert.Contains(jobs, job => job.Key == "report:reports/persisted" && job.Variables!["report"] == "reports/persisted");
        var pending = (await ExecutionsAsync(host)).Where(execution => execution.State == ExecutionState.Pending).ToList();
        Assert.Equal(2, pending.Count);
        Assert.All(jobs, job => Assert.Single(pending, execution => execution.Job == job.CanonicalName));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Outer_Create_Then_Update_Captures_Each_Projection_And_Last_Applied_Wins() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("outer-created");
        await using (var scope = host.Services.CreateAsyncScope()) {
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ScheduleDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>();
            Assert.Equal(MutationResult.Applied, await mutation.CreateAsync(report, transaction));
            report.IntervalTicks = TimeSpan.FromDays(3).Ticks;
            Assert.Equal(MutationResult.Applied, await mutation.UpdateAsync(report, transaction));
            Assert.Empty(await JobsAsync(host));
            await transaction.CommitAsync();
        }

        var job = await AssertActiveAsync(host, report, TimeSpan.FromDays(3).Ticks);
        var executions = await ExecutionsAsync(host);
        var initial = Assert.Single(executions, execution => execution.State == ExecutionState.Cancelled);
        var final = Assert.Single(executions, execution => execution.State == ExecutionState.Pending);
        Assert.Equal(job.CanonicalName, initial.Job);
        Assert.True(final.StartTime - initial.StartTime >= TimeSpan.FromDays(1.5));
        Assert.Equal(TimeSpan.FromDays(3).Ticks, (await ReportsAsync(host)).Single().IntervalTicks);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Outer_Update_Then_Delete_Ends_With_No_Active_Schedule() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("outer-deleted");
        await CreateAsync(host, report);
        await using (var scope = host.Services.CreateAsyncScope()) {
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ScheduleDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>();
            report.IntervalTicks = TimeSpan.FromDays(2).Ticks;
            Assert.Equal(MutationResult.Applied, await mutation.UpdateAsync(report, transaction));
            Assert.Equal(MutationResult.Applied, await mutation.DeleteAsync(report, transaction));
            Assert.Equal(TimeSpan.FromDays(1).Ticks, (await JobsAsync(host)).Single().IntervalTicks);
            await transaction.CommitAsync();
        }

        var job = await AssertPausedAsync(host, report);
        Assert.Equal(TimeSpan.FromDays(2).Ticks, job.IntervalTicks);
        Assert.NotNull((await ReportsAsync(host)).Single().DeleteTime);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Outer_Rollback_Leaves_Durable_Report_And_Job_State_Unchanged() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        var report = NewReport("rollback");
        await CreateAsync(host, report);
        var before = (await JobsAsync(host)).Single();
        var pending = (await ExecutionsAsync(host)).Single();
        await using (var scope = host.Services.CreateAsyncScope()) {
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ScheduleDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>();
            report.IntervalTicks = TimeSpan.FromDays(4).Ticks;
            await mutation.UpdateAsync(report, transaction);
            await mutation.CreateAsync(NewReport("rolled-back-create"), transaction);
            await transaction.RollbackAsync();
        }

        var after = await AssertActiveAsync(host, report, TimeSpan.FromDays(1).Ticks);
        Assert.Equal(before.ScheduleVersion, after.ScheduleVersion);
        Assert.Equal(before.Timestamp, after.Timestamp);
        Assert.Equal(before.NextRunTime, after.NextRunTime);
        Assert.Equal(pending.Uid, (await ExecutionsAsync(host)).Single().Uid);
        Assert.Equal(TimeSpan.FromDays(1).Ticks, (await ReportsAsync(host)).Single().IntervalTicks);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Blocked_NoWrite_Create_Update_And_SoftDelete_Leave_Scheduling_Unchanged() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        host.Control.BlockCreate = true;
        Assert.Equal(MutationResult.NoWrite, await CreateAsync(host, NewReport("blocked")));
        Assert.Empty(await ReportsAsync(host));
        Assert.Empty(await JobsAsync(host));
        host.Control.BlockCreate = false;
        var report = NewReport("kept");
        await CreateAsync(host, report);
        var before = (await JobsAsync(host)).Single();
        var pending = (await ExecutionsAsync(host)).Single();
        host.Control.BlockUpdate = true;
        report.IntervalTicks = TimeSpan.FromDays(5).Ticks;

        Assert.Equal(MutationResult.NoWrite, await UpdateAsync(host, report));
        Assert.Equal(MutationResult.NoWrite, await DeleteAsync(host, report));

        var after = await AssertActiveAsync(host, report, TimeSpan.FromDays(1).Ticks);
        Assert.Equal(before.ScheduleVersion, after.ScheduleVersion);
        Assert.Equal(pending.Uid, (await ExecutionsAsync(host)).Single().Uid);
        var stored = (await ReportsAsync(host)).Single();
        Assert.Null(stored.DeleteTime);
        Assert.Equal(TimeSpan.FromDays(1).Ticks, stored.IntervalTicks);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task Job_Write_Failures_Aggregate_While_Report_Commit_And_Later_Callback_Remain_Applied() {
        await using var host = await ReportScheduleLifecycleHost.CreateAsync();
        host.Control.RejectedPrefix = "report:reports/rejected";
        await using (var scope = host.Services.CreateAsyncScope()) {
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork<ScheduleDbContext>>();
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>();
            await mutation.CreateAsync(NewReport("rejected-one"), transaction);
            await mutation.CreateAsync(NewReport("rejected-two"), transaction);
            await mutation.CreateAsync(NewReport("accepted"), transaction);

            var failure = await Assert.ThrowsAsync<AggregateException>(() => transaction.CommitAsync());

            Assert.Equal(new[] {
                "Job write rejected: report:reports/rejected-one",
                "Job write rejected: report:reports/rejected-two",
            }, failure.InnerExceptions.Select(exception => exception.Message));
        }

        Assert.Equal(new[] { "accepted", "rejected-one", "rejected-two" },
            (await ReportsAsync(host)).Select(report => report.Name).OrderBy(name => name));
        var job = (await JobsAsync(host)).Single();
        Assert.Equal("report:reports/accepted", job.Key);
        Assert.Equal(JobState.Active, job.State);
        Assert.Equal(job.CanonicalName, (await ExecutionsAsync(host)).Single().Job);
    }

    private static ScheduleReport NewReport(string name) => new() {
        Name = name,
        Periodic = true,
        ScheduleKind = ReportScheduleKind.Periodic,
        IntervalTicks = TimeSpan.FromDays(1).Ticks,
    };

    private static async Task<MutationResult> CreateAsync(ReportScheduleLifecycleHost host, ScheduleReport report) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>().CreateAsync(report);
    }

    private static async Task<MutationResult> UpdateAsync(ReportScheduleLifecycleHost host, ScheduleReport report, Operations operation = Operations.Update) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>().UpdateAsync(report, operation: operation);
    }

    private static async Task<MutationResult> DeleteAsync(ReportScheduleLifecycleHost host, ScheduleReport report, Operations operation = Operations.Delete) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IResourceMutation<ScheduleReport>>().DeleteAsync(report, operation: operation);
    }

    private static async Task<SchemataJob> AssertActiveAsync(ReportScheduleLifecycleHost host, ScheduleReport report, long interval) {
        var job = Assert.Single(await JobsAsync(host), job => job.Key == $"report:{report.CanonicalName}");
        Assert.Equal(JobState.Active, job.State);
        Assert.Equal(interval, job.IntervalTicks);
        var pending = Assert.Single(await ExecutionsAsync(host), execution => execution.Job == job.CanonicalName && execution.State == ExecutionState.Pending);
        Assert.Equal(job.ScheduleVersion, pending.ScheduleVersion);
        Assert.Equal(job.NextRunTime, pending.StartTime);
        return job;
    }

    private static async Task<SchemataJob> AssertPausedAsync(ReportScheduleLifecycleHost host, ScheduleReport report) {
        var job = Assert.Single(await JobsAsync(host), job => job.Key == $"report:{report.CanonicalName}");
        Assert.Equal(JobState.Paused, job.State);
        Assert.DoesNotContain(await ExecutionsAsync(host), execution => execution.Job == job.CanonicalName && execution.State == ExecutionState.Pending);
        return job;
    }

    private static async Task<List<SchemataJob>> JobsAsync(ReportScheduleLifecycleHost host) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Set<SchemataJob>().AsNoTracking().ToListAsync();
    }

    private static async Task<List<SchemataJobExecution>> ExecutionsAsync(ReportScheduleLifecycleHost host) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Set<SchemataJobExecution>().AsNoTracking().ToListAsync();
    }

    private static async Task<List<ScheduleReport>> ReportsAsync(ReportScheduleLifecycleHost host) {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Set<ScheduleReport>().AsNoTracking().ToListAsync();
    }
}
