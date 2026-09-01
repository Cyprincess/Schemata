using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

/// <summary>
///     Proves the facade (<see cref="IScheduler.TriggerAsync{TJob}" />, which itself dispatches through
///     <see cref="IRequestDispatcher" />) and a raw <see cref="IRequestDispatcher" /> entry run the exact
///     same <see cref="TriggerJobRequest" /> pipeline: equivalent <see cref="SchemataJobExecution" />
///     results, the registered <see cref="ICommandAdvisor{TCommand}" /> firing once per entry, and
///     identical exception shapes when the scheduler has not started. Neither entry stubs the real
///     <c>DefaultTriggerJobHandler</c>.
/// </summary>
public sealed class SchedulingEntryEquivalenceShould
{
    [Fact]
    public async Task Trigger_Through_Facade_And_Dispatcher_Produce_Equivalent_Executions_And_Fire_The_Same_Advisor() {
        var facadeSpy     = new RecordingCommandAdvisor();
        var facadeHarness = await CreateStartedHarnessAsync(facadeSpy);
        var facadeExecution = await facadeHarness.Scheduler.TriggerAsync<SampleJob>(
            new JobContext { Job = "sample" }, CancellationToken.None);

        var dispatcherSpy     = new RecordingCommandAdvisor();
        var dispatcherHarness = await CreateStartedHarnessAsync(dispatcherSpy);
        var dispatcher         = dispatcherHarness.Services.GetRequiredService<IRequestDispatcher>();
        var dispatcherExecution = await dispatcher.SendAsync<TriggerJobRequest, SchemataJobExecution>(
            new("sample", typeof(SampleJob), new JobContext { Job = "sample" }), CancellationToken.None);

        Assert.Equal(facadeExecution.JobKey, dispatcherExecution.JobKey);
        Assert.Equal(facadeExecution.Job, dispatcherExecution.Job);
        Assert.Equal(facadeExecution.State, dispatcherExecution.State);
        Assert.Equal(facadeExecution.Method, dispatcherExecution.Method);
        Assert.Equal(facadeExecution.ArgsJson, dispatcherExecution.ArgsJson);
        Assert.Equal(facadeExecution.Variables, dispatcherExecution.Variables);
        Assert.Equal(1, facadeSpy.Count);
        Assert.Equal(1, dispatcherSpy.Count);
    }

    [Fact]
    public async Task Trigger_Throw_The_Same_Exception_Type_Through_Both_Entries_When_The_Scheduler_Is_Stopped() {
        var facadeHarness = CreateHarness(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            facadeHarness.Scheduler.TriggerAsync<SampleJob>(new JobContext { Job = "sample" }, CancellationToken.None));

        var dispatcherHarness = CreateHarness(null);
        var dispatcher         = dispatcherHarness.Services.GetRequiredService<IRequestDispatcher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.SendAsync<TriggerJobRequest, SchemataJobExecution>(
            new("sample", typeof(SampleJob), new JobContext { Job = "sample" }), CancellationToken.None));
    }

    private static async Task<Harness> CreateStartedHarnessAsync(ICommandAdvisor<TriggerJobRequest>? advisor) {
        var harness = CreateHarness(advisor);
        await harness.Scheduler.StartAsync(CancellationToken.None);
        return harness;
    }

    private static Harness CreateHarness(ICommandAdvisor<TriggerJobRequest>? advisor) {
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
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var collection = new ServiceCollection()
                        .AddSingleton(registry.Object)
                        .AddSingleton(jobs.Object)
                        .AddSingleton(executions.Object)
                        .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()));

        if (advisor is not null) {
            collection.AddSingleton(advisor);
        }

        collection.AddSchemataScheduling();
        var services = collection.BuildServiceProvider();

        harness.Services  = services;
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

        public ServiceProvider Services { get; set; } = null!;
    }

    /// <summary>Records every dispatch of <see cref="TriggerJobRequest" /> it observes.</summary>
    private sealed class RecordingCommandAdvisor : ICommandAdvisor<TriggerJobRequest>
    {
        public int Count { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, TriggerJobRequest a1, CancellationToken ct = default) {
            Count++;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
