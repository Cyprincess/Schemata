using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
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
            async () => await harness.Scheduler.TriggerAsync<SampleJob>(new JobContext(), CancellationToken.None));
    }

    [Fact]
    public async Task Trigger_ReturnsAPendingExecution_AndPersistsIt() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new JobContext { Job = "jobs/sample" }, CancellationToken.None);

        Assert.Equal(ExecutionState.Pending, execution.State);
        Assert.Same(execution, Assert.Single(harness.Persisted));
    }

    [Fact]
    public async Task Trigger_AddressesTheExecutionAsAnOperation() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new JobContext { Job = "jobs/sample" }, CancellationToken.None);

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
            new JobContext { Job = "jobs/sample", ExecutionUid = uid }, CancellationToken.None);

        // The caller owns the operation name when it supplies one, so a client that pre-computed
        // operations/{uid} can address the row it is about to create.
        Assert.Equal(uid, execution.Uid);
        Assert.Equal(uid.ToString("n"), execution.Name);
    }

    [Fact]
    public async Task Trigger_StampsTheJobKeyResolvedFromTheJobType() {
        var harness = await StartedHarness();

        var execution = await harness.Scheduler.TriggerAsync<SampleJob>(
            new JobContext { Job = "jobs/sample" }, CancellationToken.None);

        Assert.Equal("sample-key", execution.JobKey);
    }

    private static async Task<Harness> StartedHarness() {
        var harness = CreateHarness();
        await harness.Scheduler.StartAsync(CancellationToken.None);
        return harness;
    }

    private static Harness CreateHarness() {
        var harness = new Harness();

        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(r => r.ResolveKey(typeof(SampleJob))).Returns("sample-key");

        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> _, CancellationToken _) => Empty());
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns((SchemataJobExecution execution, CancellationToken _) => {
                      harness.Persisted.Add(execution);
                      return Task.CompletedTask;
                  });
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection()
                      .AddSingleton(registry.Object)
                      .AddSingleton(jobs.Object)
                      .AddSingleton(executions.Object)
                      .AddSingleton<IOptions<SchemataSchedulingOptions>>(
                           Options.Create(new SchemataSchedulingOptions()))
                      .AddSchemataScheduling()
                      .BuildServiceProvider();

        harness.Scheduler = services.GetRequiredService<DefaultScheduler>();
        return harness;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> Empty() {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class Harness
    {
        public DefaultScheduler Scheduler { get; set; } = null!;

        public List<SchemataJobExecution> Persisted { get; } = [];
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
