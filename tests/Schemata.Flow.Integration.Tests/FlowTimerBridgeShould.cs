using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
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
        Assert.Equal(
            waiting.Select(t => t.CanonicalName).OrderBy(n => n),
            jobs.Select(j => j.Variables!["tokenName"]).OrderBy(n => n));

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
    public async Task Run_Transition_Advisors_On_The_Timer_Triggered_Path() {
        var process = await StartAsync(nameof(ParallelTimerProcess));

        var jobs      = await ReadJobsAsync(process.CanonicalName!);
        var job       = jobs[0];
        var tokenName = job.Variables!["tokenName"];

        await FireAsync(job);

        var observed = _fixture.Spy.Observed
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

            execution!.StartTime = DateTime.UtcNow.AddSeconds(-1);
            await executions.UpdateAsync(execution);
            await executions.CommitAsync();
        }

        await _fixture.DispatchPendingAsync();

        using var verificationScope = _fixture.CreateScope();
        var       verification      = verificationScope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var completed = await verification.FirstOrDefaultAsync(
            query => query.Where(current => current.Job == job.CanonicalName));
        Assert.NotNull(completed);
        Assert.Equal(ExecutionState.Succeeded, completed!.State);
    }

    private async Task<List<SchemataJob>> ReadJobsAsync(string processCanonical) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        var leaf = processCanonical[(processCanonical.LastIndexOf('/') + 1)..];
        var jobs = new List<SchemataJob>();
        await foreach (var job in repository.ListAsync(
                           query => query.Where(current => current.Name!.StartsWith($"flow-{leaf}-")))) {
            jobs.Add(job);
        }

        return jobs;
    }

    private async Task<Order> CreateOrderAsync() {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<Order>>();
        var order = new Order {
            Uid           = Identifiers.NewUid(),
            Name          = Identifiers.NewUid().ToString("n"),
            CanonicalName = $"orders/{Identifiers.NewUid():n}",
            Timestamp     = Identifiers.NewUid(),
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
        return order!;
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
