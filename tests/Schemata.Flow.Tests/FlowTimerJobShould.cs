using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Scheduling.Runtime;
using Schemata.Flow.Skeleton.Models;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class FlowTimerJobShould
{
    [Fact]
    public async SystemTask MissingProcessName_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(
            new() { Variables = new Dictionary<string, string?>() }, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingTokenName_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        var context = new JobContext {
            Variables = new Dictionary<string, string?> { ["processName"] = "processes/p1" },
        };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingTimerDefinition_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        var context = new JobContext {
            Variables = new Dictionary<string, string?> {
                ["processName"] = "processes/p1",
                ["tokenName"]   = "processes/p1/tokens/t1",
            },
        };

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async SystemTask MissingElementName_Throws() {
        var job     = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());
        var context = Firing(Guid.NewGuid());
        context.Variables = new Dictionary<string, string?>(context.Variables.Where(pair => pair.Key != "elementName"));

        await Assert.ThrowsAsync<FailedPreconditionException>(() => job.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async SystemTask StaleFiring_WithoutPersistedJob_Throws() {
        var job = new FlowTimerJob(new ServiceCollection().BuildServiceProvider());

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(
            () => job.ExecuteAsync(Firing(Guid.NewGuid(), job: null), CancellationToken.None));

        AssertStale(exception);
    }

    [Fact]
    public async SystemTask StaleFiring_WhenRegistrationIsGone_Throws() {
        var job = new FlowTimerJob(ProviderReturning(null));

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(
            () => job.ExecuteAsync(Firing(Guid.NewGuid()), CancellationToken.None));

        AssertStale(exception);
    }

    [Fact]
    public async SystemTask StaleFiring_WhenRegistrationWasCancelled_Throws() {
        var version = Guid.NewGuid();
        var job     = new FlowTimerJob(ProviderReturning(Registration(JobState.Paused, version)));

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(
            () => job.ExecuteAsync(Firing(version), CancellationToken.None));

        AssertStale(exception);
    }

    [Fact]
    public async SystemTask StaleFiring_WhenRegistrationWasReplaced_Throws() {
        var job = new FlowTimerJob(ProviderReturning(Registration(JobState.Active, Guid.NewGuid())));

        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(
            () => job.ExecuteAsync(Firing(Guid.NewGuid()), CancellationToken.None));

        AssertStale(exception);
    }

    private const string JobName = "jobs/flow-p1-timer-catch-t1";

    private static JobContext Firing(Guid scheduleVersion, string? job = JobName) {
        var timerDef = JsonSerializer.Serialize(
            new TimerDefinition { Name = "t", TimerType = TimerType.Duration, TimeExpression = "PT1H" },
            SchemataJson.Default);
        return new() {
            Job = job,
            Variables = new Dictionary<string, string?> {
                ["processName"] = "processes/p1",
                ["tokenName"]   = "processes/p1/tokens/t1",
                ["elementName"] = "timer-catch",
                ["timerDef"]    = timerDef,
            },
            Execution = new() { ScheduleVersion = scheduleVersion },
        };
    }

    private static SchemataJob Registration(JobState state, Guid version) {
        return new() { CanonicalName = JobName, State = state, ScheduleVersion = version };
    }

    private static ServiceProvider ProviderReturning(SchemataJob? row) {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>> query, CancellationToken _) =>
                 query((row is null ? Enumerable.Empty<SchemataJob>() : new[] { row }).AsQueryable()).FirstOrDefault());
        return new ServiceCollection().AddSingleton(jobs.Object).BuildServiceProvider();
    }

    private static void AssertStale(FailedPreconditionException exception) {
        var info = Assert.Single(exception.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(SchemataResources.FLOW_TIMER_STALE_FIRING, info.Reason);
    }

}
