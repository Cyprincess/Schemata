using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Integration.Tests.Fixtures;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Integration.Tests;

[Trait("Category", "Integration")]
public class CustomJobEntitiesShould
{
    [Fact]
    public async Task EfCore_Persists_Triggers_And_Queries_Host_Defined_Job_Entities() {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new MutableClock(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var probe = new CustomProbeJob();

        await using var root = await BuildEfHostAsync(connection, clock, probe);

        try {
            var scheduler = root.GetRequiredService<IScheduler<MyJob, MyExecution>>();
            Assert.IsType<DefaultScheduler<MyJob, MyExecution>>(scheduler);
            Assert.IsType<DefaultOperationService<MyJob, MyExecution>>(root.GetRequiredService<IOperationService>());
            var dispatcher = root.GetRequiredService<JobExecutionDispatcher<MyJob, MyExecution>>();
            root.GetRequiredService<IScheduledJobRegistry>().Register<CustomProbeJob>(CustomProbeJob.JobKey);
            await scheduler.StartAsync(CancellationToken.None);

            var scheduled = new MyJob {
                Name         = "custom-run",
                JobKey       = CustomProbeJob.JobKey,
                ScheduleType = ScheduleType.OneTime,
                State        = JobState.Active,
                NextRunTime  = clock.Now.AddSeconds(-1),
            };
            await scheduler.ScheduleAsync(scheduled, CancellationToken.None);
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            Assert.Single(probe.Runs);

            await using (var scope = root.CreateAsyncScope()) {
                var jobs = scope.ServiceProvider.GetRequiredService<IRepository<MyJob>>();
                var stored = await jobs.FirstOrDefaultAsync(q => q.Where(row => row.Name == "custom-run"), CancellationToken.None);
                Assert.IsType<MyJob>(stored);
                Assert.False(string.IsNullOrWhiteSpace(stored!.CanonicalName));
                Assert.Equal(JobState.Completed, stored!.State);

                var executions = scope.ServiceProvider.GetRequiredService<IRepository<MyExecution>>();
                var execution = await executions.FirstOrDefaultAsync(
                    q => q.Where(row => row.JobKey == CustomProbeJob.JobKey), CancellationToken.None);
                Assert.IsType<MyExecution>(execution);
                Assert.Equal(ExecutionState.Succeeded, execution!.State);
            }

            var triggered = await scheduler.TriggerAsync<CustomProbeJob>(new() { Job = "custom-trigger" }, CancellationToken.None);
            Assert.IsType<MyExecution>(triggered);
            Assert.Equal(ExecutionState.Pending, triggered.State);

            await dispatcher.DispatchPendingAsync(CancellationToken.None);
            Assert.Equal(2, probe.Runs.Count);

            await using (var scope = root.CreateAsyncScope()) {
                var executions = scope.ServiceProvider.GetRequiredService<IRepository<MyExecution>>();
                var settled = await executions.FirstOrDefaultAsync(
                    q => q.Where(row => row.CanonicalName == triggered.CanonicalName), CancellationToken.None);
                Assert.IsType<MyExecution>(settled);
                Assert.Equal(ExecutionState.Succeeded, settled!.State);
                Assert.Equal(2, (await executions.ListAsync<MyExecution>(null, CancellationToken.None).ToListAsync(CancellationToken.None)).Count);
            }

            await scheduler.StopAsync(CancellationToken.None);
        } finally {
            await root.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task EfCore_Execution_State_Seam_Reads_Host_Defined_Execution_Without_Default_Repository() {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var clock = new MutableClock(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var probe = new CustomProbeJob();

        await using var root = await BuildEfHostAsync(connection, clock, probe);

        try {
            var cancelled = Guid.NewGuid();
            var running   = Guid.NewGuid();
            await using (var scope = root.CreateAsyncScope()) {
                var executions = scope.ServiceProvider.GetRequiredService<IRepository<MyExecution>>();
                await executions.AddAsync(new MyExecution {
                    Uid       = cancelled,
                    JobKey    = CustomProbeJob.JobKey,
                    State     = ExecutionState.Cancelled,
                    StartTime = clock.Now,
                });
                await executions.AddAsync(new MyExecution {
                    Uid       = running,
                    JobKey    = CustomProbeJob.JobKey,
                    State     = ExecutionState.Running,
                    StartTime = clock.Now,
                });
                await executions.CommitAsync();
            }

            Assert.Null(root.GetService<IRepository<SchemataJobExecution>>());

            var state   = root.GetRequiredService<IJobExecutionState>();
            var message = new MessageContext(new Dictionary<string, string?>());

            Assert.True(await state.IsCancelledAsync(cancelled, message));
            Assert.False(await state.IsCancelledAsync(running, message));
        } finally {
            await root.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task LinqToDb_Persists_Triggers_And_Queries_Host_Defined_Job_Entities() {
        var dbPath = $"{Guid.NewGuid():n}.db";
        var schema = new MappingSchema();
        schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
        var options = new DataOptions().UseSQLite($"Data Source={dbPath}").UseMappingSchema(schema);
        var clock = new MutableClock(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var probe = new CustomProbeJob();
        var services = new ServiceCollection();
        services.TryAddScoped(_ => new SchedulingDataConnection(options));
        services.TryAddSingleton<Func<SchedulingDataConnection>>(_ => () => new(options));
        services.AddRepository<MyJob, LinqToDbRepository<SchedulingDataConnection, MyJob>>();
        services.AddRepository<MyExecution, LinqToDbRepository<SchedulingDataConnection, MyExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<MyJob>, AdviceAddResourceName<MyJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<MyExecution>, AdviceAddResourceName<MyExecution>>());
        services.AddScoped<IUnitOfWork<SchedulingDataConnection>, LinqToDbUnitOfWork<SchedulingDataConnection>>();
        services.AddSingleton<TimeProvider>(clock);

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseScheduling<MyJob, MyExecution>().WithJob<CustomProbeJob>();
        builder.Invoke(services);
        services.AddSchemataSchedulingRepositoryStore<MyJob, MyExecution>();
        services.AddSingleton(probe);

        await using var root = services.BuildServiceProvider();
        using (var scope = root.CreateScope()) {
            var connection = scope.ServiceProvider.GetRequiredService<SchedulingDataConnection>();
            LinqToDbSchedulingFixture.CreateTable<MyJob>(connection);
            LinqToDbSchedulingFixture.CreateTable<MyExecution>(connection);
        }

        try {
            var scheduler = root.GetRequiredService<IScheduler<MyJob, MyExecution>>();
            var dispatcher = root.GetRequiredService<JobExecutionDispatcher<MyJob, MyExecution>>();
            root.GetRequiredService<IScheduledJobRegistry>().Register<CustomProbeJob>(CustomProbeJob.JobKey);
            await scheduler.StartAsync(CancellationToken.None);

            var scheduled = new MyJob {
                Name         = "custom-linq",
                JobKey       = CustomProbeJob.JobKey,
                ScheduleType = ScheduleType.OneTime,
                State        = JobState.Active,
                NextRunTime  = clock.Now.AddSeconds(-1),
            };
            await scheduler.ScheduleAsync(scheduled, CancellationToken.None);
            await dispatcher.DispatchPendingAsync(CancellationToken.None);

            Assert.Single(probe.Runs);

            var triggered = await scheduler.TriggerAsync<CustomProbeJob>(new() { Job = "custom-linq-trigger" }, CancellationToken.None);
            Assert.IsType<MyExecution>(triggered);
            await dispatcher.DispatchPendingAsync(CancellationToken.None);
            Assert.Equal(2, probe.Runs.Count);

            await using (var scope = root.CreateAsyncScope()) {
                var jobs = scope.ServiceProvider.GetRequiredService<IRepository<MyJob>>();
                var stored = await jobs.FirstOrDefaultAsync(q => q.Where(row => row.Name == "custom-linq"), CancellationToken.None);
                Assert.IsType<MyJob>(stored);
                Assert.Equal(JobState.Completed, stored!.State);

                var executions = scope.ServiceProvider.GetRequiredService<IRepository<MyExecution>>();
                var settled = await executions.FirstOrDefaultAsync(
                    q => q.Where(row => row.CanonicalName == triggered.CanonicalName), CancellationToken.None);
                Assert.IsType<MyExecution>(settled);
                Assert.Equal(ExecutionState.Succeeded, settled!.State);
            }

            await scheduler.StopAsync(CancellationToken.None);
        } finally {
            await root.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) {
                File.Delete(dbPath);
            }
        }
    }

    private static async Task<ServiceProvider> BuildEfHostAsync(
        SqliteConnection connection,
        MutableClock     clock,
        CustomProbeJob   probe
    ) {
        var services = new ServiceCollection();
        services.AddDbContextFactory<CustomSchedulingDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<MyJob, EfCoreRepository<CustomSchedulingDbContext, MyJob>>();
        services.AddRepository<MyExecution, EfCoreRepository<CustomSchedulingDbContext, MyExecution>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<MyJob>, AdviceAddResourceName<MyJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<MyExecution>, AdviceAddResourceName<MyExecution>>());
        services.AddScoped<IUnitOfWork<CustomSchedulingDbContext>, EfCoreUnitOfWork<CustomSchedulingDbContext>>();
        services.AddSingleton<TimeProvider>(clock);

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseScheduling<MyJob, MyExecution>().WithJob<CustomProbeJob>();
        builder.Invoke(services);
        services.AddSchemataSchedulingRepositoryStore<MyJob, MyExecution>();
        services.AddSingleton(probe);

        var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<CustomSchedulingDbContext>().Database.EnsureCreatedAsync();
        }

        return root;
    }
}
