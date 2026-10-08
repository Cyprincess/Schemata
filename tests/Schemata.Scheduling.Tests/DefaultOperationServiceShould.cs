using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultOperationServiceShould
{
    [Fact]
    public async Task Waits_Until_Terminal_State() {
        var row = CreateExecution(ExecutionState.Pending);
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
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
                              It.IsAny<CancellationToken>()), Times.Once);
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
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Pending_Row_Without_Job_Commits_The_Cancellation_Directly() {
        var row = CreateExecution(ExecutionState.Pending);
        var executions = CreateRepositoryReturning(row);
        executions.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler);

        var operation = await service.CancelAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(operation.Done);
        Assert.Equal(ExecutionState.Cancelled, row.State);
        Assert.NotNull(row.EndTime);
        scheduler.Verify(s => s.UnscheduleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        executions.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
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
        executions.Verify(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Running_Row_Cancels_Local_Execution_Without_Unscheduling() {
        var row = CreateExecution(ExecutionState.Running);
        var executions = CreateRepositoryReturning(row);
        executions.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        using var source = new CancellationTokenSource();
        var running = new ConcurrentDictionary<string, CancellationTokenSource> {
            [row.Uid.ToString("n")] = source,
        };
        var scheduler = new Mock<IScheduler>();
        var service = CreateService(executions, scheduler, running: running);

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
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
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
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataJobExecution, CancellationToken>((row, _) => {
                      row.Name = "consumer-inline";
                      row.CanonicalName = "operations/consumer-inline";
                      rows.Add(row);
                  })
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
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
        var service = CreateService(repository);
        var operation = await service.ExecuteAsync("demo", (_, _) => throw new InvalidOperationException("source failed"));
        Assert.True(operation.Done);
        Assert.Equal("source failed", operation.Error?.Message);
        repository.Verify(value => value.UpdateAsync(
            It.Is<SchemataJobExecution>(execution => execution.State == ExecutionState.Failed
                                                  && execution.RecentError == "source failed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_Persists_Cancellation_With_A_Live_Cleanup_Token_And_Rethrows() {
        var row = CreateExecution(ExecutionState.Running);
        var repository = CreateInlineRepository(row);
        var service = CreateService(repository);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await service.ExecuteAsync("demo", (_, token) => {
            cancellation.Cancel();
            throw new OperationCanceledException(token);
        }, cancellation.Token));
        repository.Verify(value => value.UpdateAsync(
            It.Is<SchemataJobExecution>(execution => execution.State == ExecutionState.Cancelled),
            It.Is<CancellationToken>(token => token.CanBeCanceled && !token.IsCancellationRequested)), Times.Once);
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateInlineRepository(SchemataJobExecution row) {
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(value => value.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataJobExecution, CancellationToken>((execution, _) => {
                      execution.Name = row.Name;
                      execution.CanonicalName = row.CanonicalName;
                  }).ReturnsAsync(MutationResult.Applied);
        repository.Setup(value => value.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MutationResult.Applied);
        WireSelfCommit(repository);
        return repository;
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateRepositoryReturning(SchemataJobExecution row) {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        WireSelfCommit(executions);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
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
    }

    private static DefaultOperationService CreateService(
        Mock<IRepository<SchemataJobExecution>> executions,
        Mock<IScheduler>?                      scheduler = null,
        TimeSpan?                               pollInterval = null,
        ConcurrentDictionary<string, CancellationTokenSource>? running = null
    ) {
        var collection = new ServiceCollection().AddSingleton(executions.Object)
                                                .AddScoped(typeof(IResourceMutation<>), typeof(ResourceMutation<>));
        if (running is not null) {
            collection.AddSingleton(running);
        }

        var services = collection.BuildServiceProvider();
        return new(
            services.GetRequiredService<IServiceScopeFactory>(),
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
