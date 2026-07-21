using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class ReportCancellationDispatcherShould
{
    [Fact]
    public async Task Dispatcher_Preserves_Cancelled_State_After_Job_Returns() {
        var storage = new CancellationStorage();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CancellingJob>("schemata.report.generate");
        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => CreateRepository(storage).Object)
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton(new CancellingJob(storage.Cancel))
                                               .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Cancelled, storage.Stored.State);
        Assert.Equal(2, storage.UpdateAttempts);
    }

    private static Mock<IRepository<SchemataJobExecution>> CreateRepository(CancellationStorage storage) {
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query, CancellationToken _) => {
                      return ToAsync(query(new[] { storage.DispatchCopy }.AsQueryable()));
                  });
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Callback<SchemataJobExecution, CancellationToken>((row, _) => storage.Update(row))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class CancellingJob(Action cancel) : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationStorage
    {
        internal CancellationStorage() {
            Stored = new() {
                Uid       = Guid.NewGuid(),
                JobKey    = "schemata.report.generate",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UtcNow.AddMinutes(-1),
                Timestamp = Guid.NewGuid(),
            };
            DispatchCopy = Copy(Stored);
        }

        internal SchemataJobExecution DispatchCopy { get; }

        internal SchemataJobExecution Stored { get; private set; }

        internal int UpdateAttempts { get; private set; }

        internal void Cancel() {
            Stored = Copy(Stored);
            Stored.State     = ExecutionState.Cancelled;
            Stored.Timestamp = Guid.NewGuid();
        }

        internal void Update(SchemataJobExecution row) {
            UpdateAttempts++;
            if (row.Timestamp != Stored.Timestamp) {
                throw new AbortedException();
            }

            Stored = Copy(row);
            Stored.Timestamp = Guid.NewGuid();
            row.Timestamp    = Stored.Timestamp;
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid       = source.Uid,
                JobKey    = source.JobKey,
                State     = source.State,
                StartTime = source.StartTime,
                Timestamp = source.Timestamp,
            };
        }
    }
}
