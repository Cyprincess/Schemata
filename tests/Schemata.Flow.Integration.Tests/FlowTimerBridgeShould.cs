using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Scheduling.Runtime;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class FlowTimerBridgeShould : IClassFixture<TimerBridgeFixture>
{
    private readonly TimerBridgeFixture _fixture;

    public FlowTimerBridgeShould(TimerBridgeFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task Fire_Each_Parallel_Timer_On_Its_Own_Token_Without_Ambiguity() {
        var process = await StartAsync(nameof(ParallelTimerProcess));

        var waiting = await ReadTokensAsync(process.Name!);
        Assert.Equal(2, waiting.Count);
        Assert.All(waiting, token => Assert.Equal("Waiting", token.State));
        Assert.Equal(["timer-a", "timer-b"], waiting.Select(t => t.WaitingAtName).OrderBy(n => n));

        var jobs = await ReadJobsAsync(process.CanonicalName!);
        Assert.Equal(2, jobs.Count);
        Assert.Equal(2, jobs.Select(j => j.Name).Distinct().Count());
        Assert.All(jobs, job => Assert.Equal(FlowTimerJob.JobKey, job.JobKey));
        Assert.Equal(
            waiting.Select(t => t.CanonicalName).OrderBy(n => n),
            jobs.Select(j => j.Variables!["tokenName"]).OrderBy(n => n));


        using (var registrationScope = _fixture.CreateScope()) {
            await registrationScope.ServiceProvider.GetRequiredService<IProcessRegistry>()
                .RegisterAsync<ProcessVersionShould.Original>(FlowConstants.Engines.Bpmn, c => {
                    c.Name = nameof(ParallelTimerProcess); c.Version = "replacement"; c.IsLatest = true;
                });
        }
        var first        = jobs[0];
        var firstToken   = first.Variables!["tokenName"];
        var otherToken   = waiting.Single(t => t.CanonicalName != firstToken).CanonicalName;

        await FireAsync(first);

        var afterFirst = await ReadTokensAsync(process.Name!);
        var fired      = afterFirst.Single(t => t.CanonicalName == firstToken);
        var untouched  = afterFirst.Single(t => t.CanonicalName == otherToken);
        Assert.Null(fired.WaitingAtName);
        Assert.Equal("Waiting", untouched.State);
        Assert.NotNull(untouched.WaitingAtName);

        await FireAsync(jobs[1]);

        var afterBoth = await ReadTokensAsync(process.Name!);
        Assert.DoesNotContain(afterBoth, token => token.State == "Waiting");
        Assert.Equal(["task-a", "task-b"], afterBoth.Select(t => t.StateName).OrderBy(n => n));
    }

    [Fact]
    public async Task Arm_Catch_Handlers_On_The_Timer_Triggered_Path() {
        var process = await StartAsync(nameof(ParallelTimerProcess));

        var jobs      = await ReadJobsAsync(process.CanonicalName!);
        var job       = jobs[0];
        var tokenName = job.Variables!["tokenName"];

        await FireAsync(job);

        var observed = _fixture.Observed
                               .Where(record => record.Process == process.CanonicalName)
                               .ToList();
        Assert.Contains(observed, record => record.Token == tokenName && record.PreviousWaitingAtName is "timer-a" or "timer-b");
    }

    [Fact]
    public async Task Project_Bound_Source_Write_Back_On_Timer_Trigger() {
        var order   = await CreateOrderAsync();
        var process = await StartWithSourceAsync(nameof(SourceTimerProcess), order);

        var job = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        await FireAsync(job);

        var persisted = await ReadOrderAsync(order.Uid);
        Assert.Equal("apply", persisted.State);
    }

    [Fact]
    public async Task Reentering_The_Gateway_Replaces_The_Timer_And_Rejects_The_Stale_Firing() {
        var definition = await RegisterAsync(typeof(ReentrantGatewayTimerProcess));
        var process    = await StartAsync(definition);

        var token = await ReadTokenAsync(process.Name!);
        Assert.Equal("gateway", token.WaitingAtName);
        var job = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Active, job.State);
        var firstVersion   = job.ScheduleVersion;
        var firstExecution = await ReadPendingExecutionAsync(job.CanonicalName!);
        Assert.NotNull(firstExecution);
        Assert.Equal(firstVersion, firstExecution.ScheduleVersion);

        await CorrelateAsync(process, "redo-message");

        // Leaving through the message branch cancelled the pending timer branch; re-entering
        // armed a fresh generation under the same job identity.
        var reentered = await ReadTokenAsync(process.Name!);
        Assert.Equal("gateway", reentered.WaitingAtName);
        var rearmed = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Active, rearmed.State);
        Assert.Equal(job.CanonicalName, rearmed.CanonicalName);
        Assert.NotEqual(firstVersion, rearmed.ScheduleVersion);
        var cancelled = await ReadExecutionAsync(firstExecution.Uid);
        Assert.Equal(ExecutionState.Cancelled, cancelled.State);
        var current = await ReadPendingExecutionAsync(rearmed.CanonicalName!);
        Assert.NotNull(current);
        Assert.Equal(rearmed.ScheduleVersion, current.ScheduleVersion);

        await AssertStaleFiringAsync(rearmed, firstVersion);
        Assert.Equal("gateway", (await ReadTokenAsync(process.Name!)).WaitingAtName);

        await FireExecutionAsync(rearmed, current);

        var completed = await ReadTokenAsync(process.Name!);
        Assert.Null(completed.WaitingAtName);
        Assert.Equal("end", completed.StateName);
    }

    [Fact]
    public async Task Terminate_Active_Host_Disarms_Its_Boundary_Timer() {
        var definition = await RegisterAsync(typeof(BoundaryTimerBridgeProcess));
        var process    = await StartAsync(definition);

        var job = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Active, job.State);
        var version = job.ScheduleVersion;

        await TerminateAsync(process);

        var disarmed = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Paused, disarmed.State);
        Assert.NotEqual(version, disarmed.ScheduleVersion);
        Assert.Null(await ReadPendingExecutionAsync(job.CanonicalName!));

        await AssertStaleFiringAsync(disarmed, version);
    }

    [Fact]
    public async Task Terminate_End_Event_Disarms_The_Sibling_Timer() {
        var definition = await RegisterAsync(typeof(TerminateEndTimerProcess));
        var process    = await StartAsync(definition);

        var tokens = await ReadTokensAsync(process.Name!);
        var worker = Assert.Single(tokens, token => token.StateName == "work");
        Assert.Single(tokens, token => token.WaitingAtName == "timer-catch");
        var job = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Active, job.State);

        await CompleteAsync(process, worker.CanonicalName);

        var disarmed = Assert.Single(await ReadJobsAsync(process.CanonicalName!));
        Assert.Equal(JobState.Paused, disarmed.State);
        Assert.Null(await ReadPendingExecutionAsync(job.CanonicalName!));
    }

    private async Task<string> RegisterAsync(Type definition) {
        using var scope    = _fixture.CreateScope();
        var       registry = scope.ServiceProvider.GetRequiredService<IProcessRegistry>();
        var       name     = $"{definition.Name}-{Guid.NewGuid():n}";
        await registry.RegisterAsync(new() {
            Name           = name,
            Engine         = FlowConstants.Engines.Bpmn,
            DefinitionType = definition,
        });
        return name;
    }

    private async Task CorrelateAsync(SchemataProcess process, string message) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.CorrelateAsync(process, message, (string?)null, null, null, CancellationToken.None);
    }

    private async Task TerminateAsync(SchemataProcess process) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.TerminateAsync(process, null, CancellationToken.None);
    }

    private async Task CompleteAsync(SchemataProcess process, string? token) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.CompleteAsync(process, token, null, CancellationToken.None);
    }

    private async Task AssertStaleFiringAsync(SchemataJob job, Guid version) {
        using var scope    = _fixture.CreateScope();
        var       timerJob = new FlowTimerJob(scope.ServiceProvider);
        var exception = await Assert.ThrowsAsync<FailedPreconditionException>(() => timerJob.ExecuteAsync(new() {
            Job       = job.CanonicalName,
            Variables = job.Variables ?? new Dictionary<string, string?>(),
            Execution = new() { ScheduleVersion = version },
        }, CancellationToken.None));
        var info = Assert.Single(exception.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(SchemataResources.FLOW_TIMER_STALE_FIRING, info.Reason);
    }

    private async Task<SchemataJobExecution?> ReadPendingExecutionAsync(string jobCanonical) {
        using var scope      = _fixture.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        return await executions.FirstOrDefaultAsync(
            query => query.Where(current => current.Job == jobCanonical && current.State == ExecutionState.Pending));
    }

    private async Task<SchemataJobExecution> ReadExecutionAsync(Guid uid) {
        using var scope      = _fixture.CreateScope();
        var       executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var       execution  = await executions.FindAsync([uid]);
        Assert.NotNull(execution);
        return execution;
    }

    private async Task<SchemataProcessToken> ReadTokenAsync(string processName) {
        var token = Assert.Single(await ReadTokensAsync(processName));
        Assert.Null(token.DeleteTime);
        return token;
    }

    private async Task FireExecutionAsync(SchemataJob job, SchemataJobExecution execution) {
        using (var scope = _fixture.CreateScope()) {
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            var pending    = await executions.FindAsync([execution.Uid]);
            Assert.NotNull(pending);
            Assert.Equal(ExecutionState.Pending, pending.State);
            pending.StartTime = DateTime.UtcNow.AddSeconds(-1);
            await executions.UpdateAsync(pending);
            await executions.CommitAsync();
        }

        await _fixture.DispatchPendingAsync();

        using var verificationScope = _fixture.CreateScope();
        var       verification      = verificationScope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var       completed         = await verification.FindAsync([execution.Uid]);
        Assert.NotNull(completed);
        Assert.Equal(ExecutionState.Succeeded, completed.State);
    }

    private async Task<SchemataProcess> StartAsync(string definitionName) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(definitionName, null, null, CancellationToken.None);
    }

    private async Task<SchemataProcess> StartWithSourceAsync(string definitionName, Order order) {
        var current = await ReadOrderAsync(order.Uid);
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(definitionName, current, null, null, CancellationToken.None);
    }

    private async Task FireAsync(SchemataJob job) {
        using (var scope = _fixture.CreateScope()) {
            var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            var execution = await executions.FirstOrDefaultAsync(
                query => query.Where(current => current.Job == job.CanonicalName && current.State == ExecutionState.Pending));
            Assert.NotNull(execution);

            execution.StartTime = DateTime.UtcNow.AddSeconds(-1);
            await executions.UpdateAsync(execution);
            await executions.CommitAsync();
        }

        await _fixture.DispatchPendingAsync();

        using var verificationScope = _fixture.CreateScope();
        var       verification      = verificationScope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var completed = await verification.FirstOrDefaultAsync(
            query => query.Where(current => current.Job == job.CanonicalName));
        Assert.NotNull(completed);
        Assert.Equal(ExecutionState.Succeeded, completed.State);
    }

    private async Task<List<SchemataJob>> ReadJobsAsync(string processCanonical) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        var leaf = processCanonical[(processCanonical.LastIndexOf('/') + 1)..];
        var jobs = new List<SchemataJob>();
        await foreach (var job in repository.ListAsync(
                           query => query.Where(current => current.Key!.StartsWith($"flow-{leaf}-")))) {
            jobs.Add(job);
        }

        return jobs;
    }

    private async Task<Order> CreateOrderAsync() {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order {
            Uid           = Guid.NewGuid(),
            Name          = Guid.NewGuid().ToString("n"),
            CanonicalName = $"orders/{Guid.NewGuid():n}",
            Timestamp     = Guid.NewGuid(),
            State         = "new",
            TaskValue     = "before",
        };

        await repository.AddAsync(order);
        await repository.CommitAsync();
        return order;
    }

    private async Task<Order> ReadOrderAsync(Guid uid) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = await repository.FindAsync([uid]);
        Assert.NotNull(order);
        return order;
    }

    private async Task<List<SchemataProcessToken>> ReadTokensAsync(string processName) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var tokens = new List<SchemataProcessToken>();
        await foreach (var token in repository.ListAsync<SchemataProcessToken>(
                           query => query.Where(t => t.Process == processName))) {
            tokens.Add(token);
        }

        return tokens;
    }
}
