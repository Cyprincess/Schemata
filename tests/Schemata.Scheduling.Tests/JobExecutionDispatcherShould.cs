using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Advisors;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class JobExecutionDispatcherShould
{
    [Fact]
    public async Task DispatchPendingAsync_MissingJobKey_MarksExecutionFailed() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/missing",
            JobKey    = "jobs.missing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-10),
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(new DefaultScheduledJobRegistry())
                                              .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(ExecutionState.Failed, execution.State);
        Assert.Contains("jobs.missing", execution.RecentError);
    }

    [Fact]
    public async Task DispatchPendingAsync_CarriesExecutionVariables_IntoJobBody() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            JobKey    = "jobs.capturing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            Variables = new() {
                ["processName"] = "processes/p1",
                ["timerDef"]    = "{\"kind\":\"duration\"}",
            },
        };

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((
                                   Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                   CancellationToken                                                        _
                               )
                               => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CapturingJob>("jobs.capturing");
        var capturing = new CapturingJob();

        var services = new ServiceCollection().AddSingleton(executions.Object)
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton(capturing)
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .BuildServiceProvider();

        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        Assert.NotNull(capturing.Captured);
        Assert.Equal("processes/p1", capturing.Captured!["processName"]);
        Assert.Equal("{\"kind\":\"duration\"}", capturing.Captured["timerDef"]);
    }

    [Fact]
    public async Task DispatchPendingAsync_PersistsSucceededState_AfterClaimCommit() {
        var storage  = new CompletionStorage();
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton<CompletingJob>()
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ExecutionState.Succeeded, persisted.State);
    }

    [Fact]
    public async Task DispatchPendingAsync_TwoDueExecutions_TransitionsBothPastPending() {
        // Two rows, because the second claim reuses the repository the first claim committed.
        var storage = new MultiExecutionStorage(
            new SchemataJobExecution {
                Uid       = Guid.Parse("2b6f0cc9-04b9-4a2f-9b8f-6c1d2e3f4a5b"),
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("aa000000-0000-0000-0000-000000000001"),
            },
            new SchemataJobExecution {
                Uid       = Guid.Parse("3c7f1dd0-15ca-4b30-ac90-7d2e3f405b6c"),
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("aa000000-0000-0000-0000-000000000002"),
            });

        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .AddSchemataScheduling()
                                              .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        var snapshot = storage.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.All(snapshot, row => Assert.Equal(ExecutionState.Succeeded, row.State));
    }

    [Fact]
    public async Task DispatchPendingAsync_UnregisteredJobKey_MarksExecutionFailed_AfterClaimCommit() {
        var storage  = new CompletionStorage();
        var registry = new DefaultScheduledJobRegistry();

        var services = new ServiceCollection().AddScoped<IRepository<SchemataJobExecution>>(_ => storage.CreateRepository())
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .BuildServiceProvider();
        var dispatcher = new JobExecutionDispatcher(services);

        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var executions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
        var persisted  = await executions.FirstOrDefaultAsync<SchemataJobExecution>(
                             query => query.Where(row => row.Uid == storage.ExecutionUid), CancellationToken.None);

        Assert.NotNull(persisted);
        Assert.Equal(ExecutionState.Failed, persisted.State);
        Assert.Contains("not registered", persisted.RecentError);
    }

    [Theory]
    [InlineData(AdviseResult.Block, ExecutionState.Blocked)]
    [InlineData(AdviseResult.Handle, ExecutionState.Skipped)]
    public async Task DispatchPendingAsync_AdvisorGate_WritesExpectedTerminalState(
        AdviseResult outcome,
        ExecutionState expected
    ) {
        var execution = new SchemataJobExecution {
            Uid = Identifiers.NewUid(), JobKey = "jobs.gated", State = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.ListAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var registry = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.gated");
        var gate = new Mock<IJobExecutionAdvisor>();
        gate.Setup(a => a.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        var observer = new Mock<IJobLifecycleObserver>();
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                               .AddSingleton<IScheduledJobRegistry>(registry)
                                               .AddSingleton<IJobExecutionAdvisor>(gate.Object)
                                               .AddSingleton<IJobLifecycleObserver>(observer.Object)
                                               .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                               .AddSchemataScheduling()
                                               .BuildServiceProvider();

        await new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(expected, execution.State);
        observer.Verify(o => o.OnBlockedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()),
                        Times.Exactly(outcome == AdviseResult.Block ? 1 : 0));
        observer.Verify(o => o.OnSkippedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<CancellationToken>()),
                        Times.Exactly(outcome == AdviseResult.Handle ? 1 : 0));
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingJobRepository_Throws() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            JobKey    = "jobs.completing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IScheduler>(Mock.Of<IScheduler>())
                                              .BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None));

        Assert.Contains(nameof(IRepository<SchemataJob>), exception.Message);
    }

    [Fact]
    public async Task DispatchPendingAsync_MissingRequestDispatcher_Throws() {
        var execution = new SchemataJobExecution {
            Uid       = Identifiers.NewUid(),
            Job       = "jobs/completing",
            JobKey    = "jobs.completing",
            State     = ExecutionState.Pending,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
        };
        var executions = ExecutionRepository(execution);
        var registry   = new DefaultScheduledJobRegistry();
        registry.Register<CompletingJob>("jobs.completing");
        var services = new ServiceCollection().AddSingleton(executions.Object)
                                              .AddSingleton<IScheduledJobRegistry>(registry)
                                              .AddSingleton<CompletingJob>()
                                              .AddSingleton<IRepository<SchemataJob>>(EmptyJobRepository())
                                              .BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new JobExecutionDispatcher(services).DispatchPendingAsync(CancellationToken.None));

        Assert.Contains(nameof(IRequestDispatcher), exception.Message);
    }

    private static Mock<IRepository<SchemataJobExecution>> ExecutionRepository(SchemataJobExecution execution) {
        var repository = new Mock<IRepository<SchemataJobExecution>>();
        repository.Setup(r => r.ListAsync(
                              It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                            CancellationToken _) => ToAsync(query(new[] { execution }.AsQueryable())));
        repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repository;
    }

    private static IRepository<SchemataJob> EmptyJobRepository() {
        var repository = new Mock<IRepository<SchemataJob>>();
        repository.Setup(r => r.FirstOrDefaultAsync(
                              It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<SchemataJob?>(null));
        return repository.Object;
    }

    private static async IAsyncEnumerable<SchemataJobExecution> ToAsync(IEnumerable<SchemataJobExecution> rows) {
        foreach (var row in rows) {
            yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class CompletionStorage
    {
        private SchemataJobExecution _stored;

        internal CompletionStorage() {
            ExecutionUid = Guid.Parse("7e4a091d-6ed5-4c3a-bafe-6f8a376c1c23");
            _stored = new() {
                Uid       = ExecutionUid,
                JobKey    = "jobs.completing",
                State     = ExecutionState.Pending,
                StartTime = DateTime.UnixEpoch,
                Timestamp = Guid.Parse("39ce6e70-9f80-4a4d-8103-ca9009fbe6aa"),
            };
        }

        internal Guid ExecutionUid { get; }

        internal IRepository<SchemataJobExecution> CreateRepository() {
            var repository = new Mock<IRepository<SchemataJobExecution>>();
            SchemataJobExecution? pending = null;

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _
                                   ) => ToAsync(query(new[] { Copy(_stored) }.AsQueryable())));
            repository.Setup(r => r.FirstOrDefaultAsync<SchemataJobExecution>(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((
                                       Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                       CancellationToken                                                        _
                                   ) => ValueTask.FromResult<SchemataJobExecution?>(
                                       query(new[] { Copy(_stored) }.AsQueryable()).FirstOrDefault()));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .Returns((SchemataJobExecution row, CancellationToken _) => {
                          pending = row;
                          return Task.CompletedTask;
                      });
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                      .Returns((CancellationToken _) => {
                          if (pending is not null) {
                              if (pending.Timestamp != _stored.Timestamp) {
                                  throw new InvalidOperationException("Concurrency token did not match the persisted execution.");
                              }

                              _stored           = Copy(pending);
                              _stored.Timestamp = Guid.NewGuid();
                              pending.Timestamp = _stored.Timestamp;
                          }

                          return Task.CompletedTask;
                      });

            return repository.Object;
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid         = source.Uid,
                JobKey      = source.JobKey,
                State       = source.State,
                StartTime   = source.StartTime,
                EndTime     = source.EndTime,
                RecentError = source.RecentError,
                Output      = source.Output,
                Timestamp   = source.Timestamp,
            };
        }
    }

    private sealed class MultiExecutionStorage
    {
        private readonly object                     _gate = new();
        private readonly List<SchemataJobExecution> _stored;

        internal MultiExecutionStorage(params SchemataJobExecution[] rows) {
            _stored = rows.Select(Copy).ToList();
        }

        internal IReadOnlyList<SchemataJobExecution> Snapshot() {
            lock (_gate) {
                return _stored.Select(Copy).ToList();
            }
        }

        internal IRepository<SchemataJobExecution> CreateRepository() {
            var                   repository = new Mock<IRepository<SchemataJobExecution>>();
            SchemataJobExecution? pending    = null;

            repository.Setup(r => r.ListAsync(
                                  It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>>(),
                                  It.IsAny<CancellationToken>()))
                      .Returns((Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>> query,
                                CancellationToken _) => ToAsync(query(Rows().AsQueryable())));
            repository.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                      .Returns((SchemataJobExecution row, CancellationToken _) => {
                          pending = row;
                          return Task.CompletedTask;
                      });
            repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                      .Returns((CancellationToken _) => {
                          if (pending is not null) {
                              lock (_gate) {
                                  var index = _stored.FindIndex(e => e.Uid == pending.Uid);
                                  if (index >= 0) {
                                      _stored[index] = Copy(pending);
                                  }
                              }
                          }

                          return Task.CompletedTask;
                      });

            return repository.Object;
        }

        private List<SchemataJobExecution> Rows() {
            lock (_gate) {
                return _stored.Select(Copy).ToList();
            }
        }

        private static SchemataJobExecution Copy(SchemataJobExecution source) {
            return new() {
                Uid         = source.Uid,
                JobKey      = source.JobKey,
                State       = source.State,
                StartTime   = source.StartTime,
                EndTime     = source.EndTime,
                RecentError = source.RecentError,
                Output      = source.Output,
                Timestamp   = source.Timestamp,
            };
        }
    }

    #region Nested type: CapturingJob

    private sealed class CompletingJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingJob : IScheduledJob
    {
        public IReadOnlyDictionary<string, string?>? Captured { get; private set; }

        public Task ExecuteAsync(JobContext context, CancellationToken ct) {
            Captured = context.Variables;
            return Task.CompletedTask;
        }
    }

    #endregion
}
