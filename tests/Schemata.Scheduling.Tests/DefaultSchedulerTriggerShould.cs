using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Characterization of <see cref="IScheduler.TriggerAsync{TJob}" /> and
///     <see cref="IScheduler.RescheduleAsync" />, the two facade methods no other test drove.
/// </summary>
/// <remarks>
///     These pin the observable facade behaviour ahead of the Scheduling command-isation, which
///     moves the orchestration out of <c>DefaultScheduler</c> into request handlers. They must keep
///     passing unchanged through that move — a diff here means the behaviour drifted, not that the
///     test was wrong.
/// </remarks>
public class DefaultSchedulerTriggerShould
{
    [Fact]
    public async Task Trigger_BeforeTheSchedulerStarts_IsRejected() {
        var harness = CreateHarness();

        // The scheduler begins stopped; a fire accepted here would never be drained.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await harness.Scheduler.TriggerAsync<SampleJob>(new(), CancellationToken.None));
    }

    [Fact]
    public async Task Trigger_ReturnsAPendingExecution_AndPersistsIt() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "jobs/sample" }, CancellationToken.None);

        Assert.Equal(ExecutionState.Pending, execution.State);
        Assert.Same(execution, Assert.Single(harness.Persisted));
    }

    [Fact]
    public async Task Trigger_AddressesTheExecutionAsAnOperation() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "jobs/sample" }, CancellationToken.None);

        Assert.Equal($"operations/{execution.Name}", execution.CanonicalName);
        Assert.Equal("jobs/sample", execution.Job);
    }

    [Fact]
    public async Task Trigger_FillsTheContextTheCallerLeftBlank() {
        var harness = await StartedHarness();
        var context = new JobContext { Job = "jobs/sample" };

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(context, CancellationToken.None);

        Assert.NotNull(context.ExecutionUid);
        Assert.NotNull(context.StartTime);
        Assert.Equal("sample-key", context.JobKey);
        Assert.Same(execution, context.Execution);
    }

    [Fact]
    public async Task Trigger_KeepsTheExecutionIdentityTheCallerSupplied() {
        var harness = await StartedHarness();
        var uid     = Guid.NewGuid();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "jobs/sample", ExecutionUid = uid }, CancellationToken.None);

        Assert.Equal(uid, execution.Uid);
        Assert.Equal("consumer-execution", execution.Name);
    }

    [Fact]
    public async Task Trigger_StampsTheJobKeyResolvedFromTheJobType() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new() { Job = "jobs/sample" }, CancellationToken.None);

        Assert.Equal("sample-key", execution.JobKey);
    }

    [Fact]
    public async Task Trigger_Resolves_Short_Name_To_Stored_Canonical_Reference() {
        var harness = CreateHarness(new() { Name = "sample", CanonicalName = "jobs/sample", ScheduleVersion = Guid.NewGuid() });
        await harness.Scheduler.StartAsync(CancellationToken.None);
        var context = new JobContext { Job = "sample" };
        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(context, CancellationToken.None);
        Assert.Equal("jobs/sample", execution.Job);
        Assert.Equal("jobs/sample", context.Job);
    }

    [Fact]
    public async Task Trigger_With_Unapplied_Execution_Creation_Throws_And_Persists_Nothing() {
        var harness = CreateHarness(executionCreateResult: MutationResult.NoWrite);
        await harness.Scheduler.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await harness.Scheduler.TriggerAsync<SampleJob>(
                new() { Job = "jobs/sample", StartTime = DateTime.UtcNow.AddHours(1) }, CancellationToken.None));

        Assert.Empty(harness.Persisted);
        Assert.Equal(0, harness.Scheduler.EntryCount);
    }

    [Fact]
    public async Task Trigger_Of_Stored_Job_Restores_The_Stored_Message_Context() {
        var harness = CreateHarness(new() {
            Name            = "sample",
            CanonicalName   = "jobs/sample",
            ScheduleVersion = Guid.NewGuid(),
            MessageContext  = new Dictionary<string, string?> { ["tenant"] = "acme" },
        });
        await harness.Scheduler.StartAsync(CancellationToken.None);

        var context    = new JobContext { Job = "jobs/sample" };
        var execution  = await harness.Scheduler.TriggerAsync<SampleJob>(context, CancellationToken.None);

        Assert.NotNull(execution.MessageContext);
        Assert.Equal("acme", execution.MessageContext["tenant"]);
    }

    private static async Task<Harness> StartedHarness() {
        var harness = CreateHarness();
        await harness.Scheduler.StartAsync(CancellationToken.None);
        return harness;
    }

    private static Harness CreateHarness(SchemataJob? persisted = null, MutationResult executionCreateResult = MutationResult.Applied) {
        var harness = new Harness();

        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(r => r.ResolveKey(typeof(SampleJob))).Returns("sample-key");

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(new ValueTask<SchemataJob?>(persisted));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.Begin()).Returns(CommittingUnit);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _, QueryOptions _) => Empty());
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync((SchemataJobExecution execution, CancellationToken _, MutationOptions _) => {
                      if (executionCreateResult != MutationResult.Applied) {
                          return executionCreateResult;
                      }

                      execution.Name = "consumer-execution";
                      execution.CanonicalName = "operations/consumer-execution";
                      harness.Persisted.Add(execution);
                      return MutationResult.Applied;
                  });
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.Begin()).Returns(CommittingUnit);

        var services = new ServiceCollection()
                      .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                      .AddSingleton(registry.Object)
                      .AddSingleton(jobs.Object)
                      .AddSingleton(executions.Object)
                      .AddSingleton<IOptions<SchemataSchedulingOptions>>(
                           Options.Create(new SchemataSchedulingOptions()))
                      .AddSchemataScheduling()
                      .BuildServiceProvider();

        harness.Scheduler = services.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        return harness;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }

    private static IUnitOfWork CommittingUnit() {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return unit.Object;
    }

    private sealed class Harness
    {
        public DefaultScheduler<SchemataJob, SchemataJobExecution> Scheduler { get; set; } = null!;

        public List<SchemataJobExecution> Persisted { get; } = [];
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
