using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;
using Schemata.Actor.Scheduling.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository.Advisors;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Actor.Scheduling.Tests;

/// <summary>
///     Exercises the reminder pipeline end to end against a real <see cref="EfCoreRepository{TContext,TEntity}" />
///     over an in-memory SQLite database and the real <see cref="IScheduler" /> timer, proving the
///     bridge works with a genuinely durable schedule rather than a manually-driven dispatch pass.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReminderShould
{
    [Fact]
    public async Task ScheduleAsync_OneTimeReminder_DeliversPayloadToTheTargetActorWithinFiveSeconds() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var root = await BuildHostAsync(connection);

        var scheduler = root.GetRequiredService<IScheduler>();
        await scheduler.StartAsync(CancellationToken.None);

        var dispatcher = root.GetRequiredService<JobExecutionDispatcher>();
        await dispatcher.StartAsync(CancellationToken.None);

        try {
            var reminders = root.GetRequiredService<IActorReminders>();
            var target    = new ActorId("recorder", "reminder-1");
            var payload   = new ReminderPayload("wake up");

            await reminders.ScheduleAsync(target, payload, TimeSpan.FromMilliseconds(500), "welcome", CancellationToken.None);

            var system = root.GetRequiredService<IActorSystem>();
            var actor  = await system.GetAsync(target);

            ReminderPayload? received = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (received is null && DateTime.UtcNow < deadline) {
                received = await actor.AskAsync<GetReceived, ReminderPayload?>(new());
                if (received is null) {
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                }
            }

            Assert.Equal(payload, received);
        } finally {
            await dispatcher.StopAsync(CancellationToken.None);
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Update_And_Cancel_Only_The_Matching_Reminder_With_Consumer_Names() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        await using var root = await BuildHostAsync(connection);
        var scheduler = root.GetRequiredService<IScheduler>();
        await scheduler.StartAsync(CancellationToken.None);
        try {
            var reminders = root.GetRequiredService<IActorReminders>();
            var target = new ActorId("recorder", "a-b");
            var otherTarget = new ActorId("recorder", "a");
            await reminders.ScheduleAsync(target, new ReminderPayload("first"), TimeSpan.FromHours(1), "c");
            await reminders.ScheduleAsync(otherTarget, new ReminderPayload("other"), TimeSpan.FromHours(1), "b-c");

            Guid original;
            string? originalName;
            await using (var scope = root.CreateAsyncScope()) {
                var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                var jobs = await database.Jobs.AsNoTracking().ToListAsync();
                Assert.Equal(2, jobs.Count);
                var job = Assert.Single(jobs, job => job.Variables!["actorKey"] == target.Key);
                original = job.Uid;
                originalName = job.Name;
                Assert.Equal($"consumer-{job.Uid:N}", job.Name);
            }

            var replacement = new ReminderPayload("replacement");
            await reminders.ScheduleAsync(target, replacement, TimeSpan.FromHours(2), "c");
            await reminders.CancelAsync(target, "c");

            await using var verifyScope = root.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
            var persisted = await verify.Jobs.AsNoTracking().ToListAsync();
            Assert.Equal(2, persisted.Count);
            var updated = Assert.Single(persisted, job => job.Uid == original);
            Assert.Equal(originalName, updated.Name);
            Assert.Equal(JobState.Paused, updated.State);
            Assert.Equal(replacement, JsonSerializer.Deserialize<ReminderPayload>(updated.Variables!["payloadJson"]!, SchemataJson.Default));
            var other = Assert.Single(persisted, job => job.Uid != original);
            Assert.Equal(JobState.Active, other.State);
            var executions = await verify.Executions.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(executions, execution => execution.Job == updated.CanonicalName
                                                       && execution.State == ExecutionState.Pending);
            Assert.Contains(executions, execution => execution.Job == other.CanonicalName
                                                  && execution.State == ExecutionState.Pending);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reject_Reminder_Without_Consumer_Name_Advisor() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        await using var root = await BuildHostAsync(connection, nameAdvisors: false);
        var scheduler = root.GetRequiredService<IScheduler>();
        await scheduler.StartAsync(CancellationToken.None);
        try {
            var reminders = root.GetRequiredService<IActorReminders>();
            await Assert.ThrowsAsync<ValidationException>(() => reminders.ScheduleAsync(
                new("recorder", "unnamed"), new ReminderPayload("missing"), TimeSpan.FromHours(1), "welcome"));
            await using var scope = root.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            Assert.Empty(await database.Jobs.ToListAsync());
            Assert.Empty(await database.Executions.ToListAsync());
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<ServiceProvider> BuildHostAsync(SqliteConnection connection, bool nameAdvisors = true) {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
        if (nameAdvisors) {
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, SchedulingNameAdvisor<SchemataJob>>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, SchedulingNameAdvisor<SchemataJobExecution>>());
        }
        services.AddSchemataScheduling();
        var actorBuilder = new SchemataActorBuilder(new(), services);
        actorBuilder.Register<RecordingActor>("recorder");
        services.AddSchemataActor();
        new SchemataActorSchedulingFeature().ConfigureServices(
            services, new(), new(), new ConfigurationBuilder().Build(), null!);
        var root = services.BuildServiceProvider();
        await using (var scope = root.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }
        var jobRegistry = root.GetRequiredService<IScheduledJobRegistry>();
        var options = root.GetRequiredService<IOptions<SchemataSchedulingOptions>>();
        jobRegistry.RegisterAll(options.Value.Jobs.Select(job => job.JobType));
        return root;
    }
}
