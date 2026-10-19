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
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Services;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultOperationServiceShould
{
    [Fact]
    public async Task Lost_Lease_Cancels_Blocked_Body_Context_Restore_Before_Callback_Entry() {
        var callbacks = new List<(TimerCallback Callback, object? State, TimeSpan Due)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UnixEpoch);
        clock.Setup(t => t.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, due, _) => {
                callbacks.Add((callback, state, due));
                return Mock.Of<ITimer>();
            });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restores = 0;
        var propagator = new Mock<IMessageContextPropagator>();
        propagator.Setup(p => p.RestoreAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyDictionary<string, string?> _, IServiceProvider _, CancellationToken token) => {
                if (Interlocked.Increment(ref restores) != 3) return ValueTask.CompletedTask;
                entered.SetResult();
                return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, token));
            });
        var mutation = new Mock<IResourceMutation<SchemataJobExecution>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
            .Callback<SchemataJobExecution, IUnitOfWork?, CancellationToken>((row, _, _) => {
                row.Uid = Guid.NewGuid(); row.Name = "blocked"; row.CanonicalName = "operations/blocked";
            }).ReturnsAsync(MutationResult.Applied);
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TryRenewLeaseAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await using var services = new ServiceCollection().AddSingleton(mutation.Object).AddSingleton(store.Object)
            .AddSingleton(propagator.Object).AddSingleton<IActiveExecutionRegistry>(new ActiveExecutionRegistry())
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        var service = new DefaultOperationService<SchemataJob, SchemataJobExecution>(services, Options.Create(new SchemataSchedulingOptions { ExecutionLease = TimeSpan.FromSeconds(10) }),
            Mock.Of<IScheduler>(), clock.Object);
        var called = false;
        var work = service.ExecuteAsync("blocked", (_, _) => { called = true; return ValueTask.FromResult<string?>(null); }).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var renewal = Assert.Single(callbacks, callback => callback.Due == TimeSpan.FromSeconds(5));
        renewal.Callback(renewal.State);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(called);
        store.Verify(s => s.TrySettleAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Waits_Until_Terminal_State() {
        var row = CreateExecution(ExecutionState.Pending);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _, QueryOptions _) => {
                      var source = Volatile.Read(ref completed) == 0
                          ? row
                          : new() {
                              Uid           = row.Uid,
                              Name          = row.Name,
                              CanonicalName = row.CanonicalName,
                              Method        = row.Method,
                              State         = row.State,
                              StartTime     = row.StartTime,
                              EndTime       = row.EndTime,
                              Output        = row.Output,
                          };
                      Assert.NotNull(predicate);
                      var snapshot = predicate(new[] { source }.AsQueryable()).SingleOrDefault();
                      firstRead.TrySetResult();

                      return new(snapshot);
                  });
        var service = CreateService(executions, pollInterval: TimeSpan.FromMilliseconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var wait = service.WaitAsync(row.CanonicalName!, cts.Token).AsTask();
        var complete = Task.Run(async () => {
            // Timeout guard only: bounds the handshake so a stuck service fails instead of hanging CI.
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(1));
            row.EndTime = DateTime.UtcNow;
            row.Output  = "{\"complete\":true}";
            row.State   = ExecutionState.Succeeded;
            Interlocked.Exchange(ref completed, 1);
        });

        var operation = await wait;
        await complete;

        Assert.True(operation.Done);
        Assert.Equal("{\"complete\":true}", operation.Response?.Output);
    }

    [Fact]
    public async Task Get_Pending_Row_Returns_Not_Done_Snapshot() {
        var row = CreateExecution(ExecutionState.Pending);
        var executions = CreateRepositoryReturning(row);
        var service = CreateService(executions);

        var operation = await service.GetAsync(row.CanonicalName!, CancellationToken.None);

        Assert.False(operation.Done);
        Assert.Equal(row.Name, operation.Name);
        executions.Verify(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_Pending_Row_Delegates_The_Transition_To_The_Unschedule_Owner() {
        var row = CreateExecution(ExecutionState.Pending);
        row.Job = "jobs/report";
        var executions = CreateRepositoryReturning(row);
        var scheduler = new Mock<IScheduler>();
        // The unschedule handler commits the pending rows' Cancelled state in its own transaction.
        scheduler.Setup(s => s.UnscheduleAsync(row.Job, It.IsAny<CancellationToken>()))
                 .Callback(() => {
                      row.State   = ExecutionState.Cancelled;
                      row.EndTime = DateTime.UtcNow;
                  })
                 .Returns(Task.CompletedTask);
        var service = CreateService(executions, scheduler);

        var operation = await service.CancelAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(operation.Done);
        scheduler.Verify(s => s.UnscheduleAsync(row.Job, It.IsAny<CancellationToken>()), Times.Once);
        // The service must not write the row a second time with the stamp the unschedule rotated.
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Pending_Row_Without_Job_Commits_The_Cancellation_Directly() {
        var row = CreateExecution(ExecutionState.Pending);
        var executions = CreateRepositoryReturning(row);
        executions.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler);

        var operation = await service.CancelAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(operation.Done);
        Assert.Equal(ExecutionState.Cancelled, row.State);
        Assert.NotNull(row.EndTime);
        scheduler.Verify(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        executions.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_On_Terminal_Row_Throws_Failed_Precondition() {
        var row = CreateExecution(ExecutionState.Succeeded);
        var executions = CreateRepositoryReturning(row);
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler);

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await service.CancelAsync(row.CanonicalName!, CancellationToken.None));

        scheduler.Verify(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
    }

    [Theory]
    [InlineData(ExecutionState.Blocked)]
    [InlineData(ExecutionState.Skipped)]
    public async Task Settled_Gate_Outcomes_Read_As_Done_Operations(ExecutionState settled) {
        var row = CreateExecution(settled);
        var executions = CreateRepositoryReturning(row);
        var service = CreateService(executions);

        var operation = await service.GetAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(operation.Done);
        Assert.NotNull(operation.Error);
    }

    [Theory]
    [InlineData(ExecutionState.Blocked)]
    [InlineData(ExecutionState.Skipped)]
    public async Task Cancel_On_Settled_Gate_Outcome_Throws_Failed_Precondition(ExecutionState settled) {
        var row = CreateExecution(settled);
        var executions = CreateRepositoryReturning(row);
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler);

        await Assert.ThrowsAsync<FailedPreconditionException>(
            async () => await service.CancelAsync(row.CanonicalName!, CancellationToken.None));

        scheduler.Verify(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
    }

    [Theory]
    [InlineData(ExecutionState.Blocked)]
    [InlineData(ExecutionState.Skipped)]
    public async Task Wait_On_Settled_Gate_Outcome_Returns_The_Done_Operation(ExecutionState settled) {
        var row = CreateExecution(settled);
        var executions = CreateRepositoryReturning(row);
        var service = CreateService(executions);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var operation = await service.WaitAsync(row.CanonicalName!, cts.Token);

        Assert.True(operation.Done);
        Assert.NotNull(operation.Error);
    }

    [Fact]
    public async Task Cancel_Running_Row_Cancels_Local_Execution_Without_Unscheduling() {
        var row = CreateExecution(ExecutionState.Running);
        var executions = CreateRepositoryReturning(row);
        executions.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        using var source = new CancellationTokenSource();
        var running = new ActiveExecutionRegistry();
        using var registration = running.Register(row.Uid.ToString("n"), source);
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler, registry: running);

        await service.CancelAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(source.IsCancellationRequested);
        Assert.Equal(ExecutionState.Cancelled, row.State);
        scheduler.Verify(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Wait_Honors_Cancellation_Token() {
        var row = CreateExecution(ExecutionState.Pending);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _, QueryOptions _) => {
                      Assert.NotNull(predicate);
                      firstRead.TrySetResult();
                      return new(
                          predicate(new[] { row }.AsQueryable()).SingleOrDefault());
                  });
        var service = CreateService(executions, pollInterval: TimeSpan.FromMilliseconds(5));
        using var cts = new CancellationTokenSource();

        var wait = service.WaitAsync(row.CanonicalName!, cts.Token).AsTask();
        // Timeout guard only: bounds the handshake so a stuck service fails instead of hanging CI.
        await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await wait);
    }

    [Fact]
    public async Task Execute_Exposes_Persisted_Identity_Before_Work_And_Completes_Same_Row() {
        var rows = new List<SchemataJobExecution>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        WireSelfCommit(executions);
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((row, _, _) => {
                      row.Name = "consumer-inline";
                      row.CanonicalName = "operations/consumer-inline";
                      rows.Add(row);
                  })
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _, QueryOptions _) => {
                      Assert.NotNull(predicate);
                      return new(
                          predicate(rows.AsQueryable()).SingleOrDefault());
                  });
        var service = CreateService(executions);

        var created = await service.ExecuteAsync("demo", (operation, _) => {
            Assert.Equal("operations/consumer-inline", operation.CanonicalName);
            Assert.Equal(ExecutionState.Running, Assert.Single(rows).State);
            return ValueTask.FromResult<string?>("{}");
        }, CancellationToken.None);
        var loaded = await service.GetAsync(created.CanonicalName!, CancellationToken.None);

        var persisted = Assert.Single(rows);
        Assert.Equal("consumer-inline", persisted.Name);
        Assert.Equal("operations/consumer-inline", persisted.CanonicalName);
        Assert.Equal(ExecutionState.Succeeded, persisted.State);
        Assert.True(created.Done);
        Assert.Equal("{}", created.Response?.Output);
        Assert.True(loaded.Done);
        Assert.Equal(created.Name, loaded.Name);
    }

    [Fact]
    public async Task Execute_Records_Callback_Failure_On_The_Persisted_Operation() {
        var row = CreateExecution(ExecutionState.Running);
        var repository = CreateInlineRepository(row);
        var failedWrites = 0;
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((execution, _, _) => {
                if (execution.State == ExecutionState.Failed && execution.RecentError == "source failed") failedWrites++;
            }).ReturnsAsync(MutationResult.Applied);
        var service = CreateService(repository);
        var operation = await service.ExecuteAsync("demo", (_, _) => throw new InvalidOperationException("source failed"));
        Assert.True(operation.Done);
        Assert.Equal("source failed", operation.Error?.Message);
        Assert.Equal(1, failedWrites);
    }

    [Fact]
    public async Task Execute_Persists_Cancellation_With_A_Live_Cleanup_Token_And_Rethrows() {
        var row = CreateExecution(ExecutionState.Running);
        var repository = CreateInlineRepository(row);
        var cancellationWrites = 0;
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((execution, token, _) => {
                if (execution.State == ExecutionState.Cancelled) {
                    Assert.True(token.CanBeCanceled && !token.IsCancellationRequested);
                    cancellationWrites++;
                }
            }).ReturnsAsync(MutationResult.Applied);
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.ExecuteAsync("demo", (_, token) => {
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        }, cancellation.Token));
        Assert.Equal(1, cancellationWrites);
    }

    [Fact]
    public async Task Cancel_During_Creation_Scope_Disposal_Prevents_Callback_Entry() {
        var repository = CreateInlineRepository(CreateExecution(ExecutionState.Running));
        SchemataJobExecution? saved = null;
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((row, _, _) => {
                row.Name = "teardown"; row.CanonicalName = "operations/teardown"; saved = row;
            }).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(() => ValueTask.FromResult(saved));
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = new Mock<IResourceMutation<SchemataJobExecution>>();
        mutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
            .Returns(async (SchemataJobExecution row, IUnitOfWork? _, CancellationToken token) => {
                await repository.Object.AddAsync(row, token); return MutationResult.Applied;
            });
        var disposed = 0;
        mutation.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(() => {
            if (Interlocked.Increment(ref disposed) != 1) return ValueTask.CompletedTask;
            disposing.SetResult(); return new ValueTask(release.Task);
        });
        await using var services = new ServiceCollection().AddSingleton(repository.Object)
            .AddSingleton<IActiveExecutionRegistry>(new ActiveExecutionRegistry())
            .AddScoped<IResourceMutation<SchemataJobExecution>>(_ => mutation.Object)
            .AddSchemataSchedulingRepositoryStore()
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        var service = new DefaultOperationService<SchemataJob, SchemataJobExecution>(services, Options.Create(new SchemataSchedulingOptions()), Mock.Of<IScheduler>());
        var entered = false;
        var work = service.ExecuteAsync("teardown", (_, _) => { entered = true; return ValueTask.FromResult<string?>(null); }).AsTask();
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CancelAsync("operations/teardown");
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.False(entered);
        Assert.Equal(ExecutionState.Cancelled, saved!.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_Renewal_Race_Reaches_Token_And_Preserves_Terminal_Winner(bool completed) {
        var first = CreateExecution(ExecutionState.Running);
        var latest = CreateExecution(completed ? ExecutionState.Succeeded : ExecutionState.Running);
        var reads = 0;
        var repository = CreateRepositoryReturning(first);
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(() => ValueTask.FromResult<SchemataJobExecution?>(++reads == 1 ? first : latest));
        using var source = new CancellationTokenSource();
        var running = new ActiveExecutionRegistry();
        using var registration = running.Register(first.Uid.ToString("n"), source);
        var settlements = 0;
        var store = new Mock<IJobExecutionStore<SchemataJob, SchemataJobExecution>>();
        store.Setup(s => s.TrySettleAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
            .Returns((SchemataJobExecution row, CancellationToken _) => {
                Assert.True(source.IsCancellationRequested);
                return Task.FromResult(++settlements != 1);
            });
        await using var services = new ServiceCollection().AddSingleton(repository.Object).AddSingleton<IActiveExecutionRegistry>(running)
            .AddSingleton(store.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        var service = new DefaultOperationService<SchemataJob, SchemataJobExecution>(services,
            Options.Create(new SchemataSchedulingOptions()), Mock.Of<IScheduler>());
        var result = await service.CancelAsync(first.CanonicalName!);
        Assert.True(source.IsCancellationRequested);
        Assert.True(result.Done);
        Assert.Equal(completed ? ExecutionState.Succeeded : ExecutionState.Cancelled, latest.State);
        Assert.Equal(completed ? 1 : 2, settlements);
    }

    [Fact]
    public async Task Refused_Inline_Creation_Never_Enters_Callback() {
        var repository = CreateInlineRepository(CreateExecution(ExecutionState.Running));
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .ReturnsAsync(MutationResult.NoWrite);
        var entered = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateService(repository).ExecuteAsync("demo", (_, _) => {
            entered = true;
            return ValueTask.FromResult<string?>(null);
        }));
        Assert.False(entered);
    }

    [Fact]
    public async Task Execute_Unrelated_Cancellation_Is_An_Ordinary_Failure() {
        var repository = CreateInlineRepository(CreateExecution(ExecutionState.Running));
        var operation = await CreateService(repository).ExecuteAsync("demo", (_, _) => throw new OperationCanceledException("independent"));
        Assert.True(operation.Done);
        Assert.Equal("independent", operation.Error?.Message);
    }

    [Fact]
    public async Task Cancel_Inline_Operation_Reaches_Callback_And_Preserves_Cancelled_Row() {
        SchemataJobExecution? saved = null;
        var repository = CreateInlineRepository(CreateExecution(ExecutionState.Running));
        repository.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
            .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((row, _, _) => {
                row.Name = "inline"; row.CanonicalName = "operations/inline"; saved = row;
            }).ReturnsAsync(MutationResult.Applied);
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns(() => ValueTask.FromResult(saved));
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = CreateService(repository);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = service.ExecuteAsync("demo", async (_, token) => {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(saved!.LeaseExpireTime);
        var cancelled = await service.CancelAsync("operations/inline");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.True(cancelled.Done);
        Assert.Equal(ExecutionState.Cancelled, saved.State);
        Assert.Null(saved.LeaseExpireTime);
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateInlineRepository(SchemataJobExecution row) {
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(value => value.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .Callback<SchemataJobExecution, CancellationToken, MutationOptions>((execution, _, _) => {
                      execution.Name = row.Name;
                      execution.CanonicalName = row.CanonicalName;
                  }).ReturnsAsync(MutationResult.Applied);
        repository.Setup(value => value.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()))
                  .ReturnsAsync(MutationResult.Applied);
        WireSelfCommit(repository);
        return repository;
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateRepositoryReturning(SchemataJobExecution row) {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        WireSelfCommit(executions);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _, QueryOptions _) => {
                      Assert.NotNull(predicate);
                      return new(
                          predicate(new[] { row }.AsQueryable()).SingleOrDefault());
                  });
        return executions;
    }

    private static void WireSelfCommit(Mock<IRepository<SchemataJobExecution>> repository) {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        repository.Setup(r => r.Begin()).Returns(unit.Object);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private static DefaultOperationService<SchemataJob, SchemataJobExecution> CreateService(
        Mock<IRepository<SchemataJobExecution>> executions,
        Mock<IScheduler>?                      scheduler = null,
        TimeSpan?                               pollInterval = null,
        IActiveExecutionRegistry?                registry = null
    ) {
        var collection = new ServiceCollection().AddSingleton(executions.Object)
                                                .AddSingleton(registry ?? new ActiveExecutionRegistry())
                                                .AddSchemataSchedulingRepositoryStore()
                                                .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>))
                                                .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>();

        var services = collection.BuildServiceProvider();
        return new(
            services,
            Options.Create(new SchemataSchedulingOptions {
                OperationPollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
            }),
            (scheduler ?? new Mock<IScheduler>()).Object
        );
    }

    private static SchemataJobExecution CreateExecution(ExecutionState state) {
        var uid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new() {
            Uid           = uid,
            Name          = uid.ToString("n"),
            CanonicalName = $"operations/{uid:n}",
            Method        = "generate",
            State         = state,
            StartTime     = DateTime.UtcNow,
        };
    }
}
