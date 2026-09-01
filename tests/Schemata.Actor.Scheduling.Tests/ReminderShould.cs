using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;
using Schemata.Actor.Scheduling.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
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
public sealed class ReminderShould
{
    [Fact]
    public async Task ScheduleAsync_OneTimeReminder_DeliversPayloadToTheTargetActorWithinFiveSeconds() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options
                     .UseSqlite(connection)
                     .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<TestDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<TestDbContext, SchemataJobExecution>>();
        services.AddSchemataScheduling();

        var actorBuilder = new SchemataActorBuilder(new SchemataOptions(), services);
        actorBuilder.Register<RecordingActor>("recorder");
        services.AddSchemataActor();

        new SchemataActorSchedulingFeature().ConfigureServices(
            services, new SchemataOptions(), new Configurators(), new ConfigurationBuilder().Build(), null!);

        await using var root = services.BuildServiceProvider();

        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Populate the job-type registry the same way SchedulingInitializer does at host startup,
        // then start the real in-memory scheduler timer.
        var jobRegistry     = root.GetRequiredService<IScheduledJobRegistry>();
        var schedulingOptions = root.GetRequiredService<IOptions<SchemataSchedulingOptions>>();
        jobRegistry.RegisterAll(schedulingOptions.Value.Jobs.Select(job => job.JobType));
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
                received = await actor.AskAsync<GetReceived, ReminderPayload?>(new GetReceived());
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
}
