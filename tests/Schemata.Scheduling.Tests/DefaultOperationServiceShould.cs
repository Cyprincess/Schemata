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
        var firstRead = new ManualResetEventSlim();
        var completed = 0;
        var reads = 0;
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
                      var source = Volatile.Read(ref completed) == 0
                          ? row
                          : new SchemataJobExecution {
                              Uid           = row.Uid,
                              Name          = row.Name,
                              CanonicalName = row.CanonicalName,
                              Method        = row.Method,
                              State         = row.State,
                              StartTime     = row.StartTime,
                              EndTime       = row.EndTime,
                              Output        = row.Output,
                          };
                      var snapshot = predicate!(new[] { source }.AsQueryable()).SingleOrDefault();
                      if (Interlocked.Increment(ref reads) == 1) {
                          firstRead.Set();
                      }

                      return new ValueTask<SchemataJobExecution?>(snapshot);
                  });
        var service = CreateService(executions, pollInterval: TimeSpan.FromMilliseconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var wait = service.WaitAsync(row.CanonicalName!, cts.Token).AsTask();
        var complete = Task.Run(() => {
            Assert.True(firstRead.Wait(TimeSpan.FromSeconds(1)));
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
    public async Task Get_Returns_Snapshot_Without_Waiting() {
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
    public async Task Cancel_Marks_Row_And_Unschedules() {
        var row = CreateExecution(ExecutionState.Running);
        row.Job = "jobs/report";
        var executions = CreateRepositoryReturning(row);
        executions.Setup(r => r.UpdateAsync(row, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.UnscheduleAsync(row.Job, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = CreateService(executions, scheduler);

        var operation = await service.CancelAsync(row.CanonicalName!, CancellationToken.None);

        Assert.True(operation.Done);
        Assert.Equal(ExecutionState.Cancelled, row.State);
        Assert.NotNull(row.EndTime);
        scheduler.Verify(s => s.UnscheduleAsync(row.Job, It.IsAny<CancellationToken>()), Times.Once);
        executions.Verify(r => r.UpdateAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        executions.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Wait_Honors_Cancellation_Token() {
        var row = CreateExecution(ExecutionState.Pending);
        var firstRead = new ManualResetEventSlim();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) => {
                      firstRead.Set();
                      return new ValueTask<SchemataJobExecution?>(
                          predicate!(new[] { row }.AsQueryable()).SingleOrDefault());
                  });
        var service = CreateService(executions, pollInterval: TimeSpan.FromMilliseconds(5));
        using var cts = new CancellationTokenSource();

        var wait = service.WaitAsync(row.CanonicalName!, cts.Token).AsTask();
        Assert.True(await Task.Run(() => firstRead.Wait(TimeSpan.FromSeconds(1))));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await wait);
    }

    [Fact]
    public async Task Create_Terminal_Writes_Addressable_Done_Row() {
        var rows = new List<SchemataJobExecution>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataJobExecution, CancellationToken>((row, _) => rows.Add(row))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) =>
                      new ValueTask<SchemataJobExecution?>(predicate!(rows.AsQueryable()).SingleOrDefault()));
        var service = CreateService(executions);
        var uid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var created = await service.CreateTerminalAsync("demo", "{}", null, uid, CancellationToken.None);
        var loaded = await service.GetAsync(created.CanonicalName!, CancellationToken.None);

        var persisted = Assert.Single(rows);
        Assert.Equal(uid, persisted.Uid);
        Assert.Equal(uid.ToString("n"), persisted.Name);
        Assert.Equal($"operations/{uid:n}", persisted.CanonicalName);
        Assert.Equal(ExecutionState.Succeeded, persisted.State);
        Assert.True(created.Done);
        Assert.Equal("{}", created.Response?.Output);
        Assert.True(loaded.Done);
        Assert.Equal(created.Name, loaded.Name);
        executions.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateRepositoryReturning(SchemataJobExecution row) {
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>? predicate,
                            CancellationToken _) =>
                      new ValueTask<SchemataJobExecution?>(predicate!(new[] { row }.AsQueryable()).SingleOrDefault()));
        return executions;
    }

    private static DefaultOperationService CreateService(
        Mock<IRepository<SchemataJobExecution>> executions,
        Mock<IScheduler>?                      scheduler = null,
        TimeSpan?                               pollInterval = null
    ) {
        var services = new ServiceCollection().AddSingleton(executions.Object).BuildServiceProvider();
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
