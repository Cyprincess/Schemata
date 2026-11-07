using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Resource.Foundation;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Scheduling.Handlers;
using Schemata.Push.Scheduling.Runtime;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

public class ScheduledPushServiceShould
{
    [Theory]
    [Trait("Layer", "Integration")]
    [InlineData(null)]
    [InlineData("reports/daily")]
    public async Task Scheduled_Send_Persists_Only_Explicit_Subject(string? subject) {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContextFactory<ScheduleDbContext>(options => options.UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataJob, EfCoreRepository<ScheduleDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<ScheduleDbContext, SchemataJobExecution>>();
        services.AddScoped<IUnitOfWork<ScheduleDbContext>, EfCoreUnitOfWork<ScheduleDbContext>>();
        var registry = new ResourceRegistry();
        registry.Add(new ResourceAttribute<SchemataJob>(), []);
        registry.Add(new ResourceAttribute<SchemataJobExecution>(), []);
        services.AddSingleton(registry);
        services.AddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
        var names = new Mock<IRepositoryAddAdvisor<SchemataJobExecution>>();
        names.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<IRepository<SchemataJobExecution>>(),
                It.IsAny<SchemataJobExecution>(), It.IsAny<MutationOptions>(), It.IsAny<CancellationToken>()))
            .Callback<AdviceContext, IRepository<SchemataJobExecution>, SchemataJobExecution, MutationOptions, CancellationToken>(
                (_, _, row, _, _) => row.Name = "consumer-send")
            .ReturnsAsync(AdviseResult.Continue);
        services.AddSingleton(names.Object);
        services.AddSchemataScheduling();
        services.AddSchemataPush();
        services.AddSingleton<IRequestHandler<SchedulePushRequest, Operation>, SchedulePushHandler>();
        await using var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Database.EnsureCreatedAsync();
        var scheduler = provider.GetRequiredService<DefaultScheduler<SchemataJob, SchemataJobExecution>>();
        provider.GetRequiredService<IScheduledJobRegistry>().Register<PushDispatchJob>("push.dispatch");
        await scheduler.StartAsync(CancellationToken.None);
        try {
            var context = new PushContext(new { Subject = "reports/payload" }, new TopicTarget("tenants/acme")) {
                Subject = subject, Metadata = new System.Collections.Generic.Dictionary<string, string?> { ["subject"] = "reports/metadata" },
            };
            var service = new ScheduledPushService(provider.GetRequiredService<IRequestDispatcher>());
            var operation = await service.ScheduleSendAsync(context, DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
            Assert.Equal(subject, operation.Metadata?.Subject);
            using var scope = provider.CreateScope();
            var rows = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();
            var row = await rows.FirstOrDefaultAsync(query => query.Where(value => value.CanonicalName == operation.CanonicalName), CancellationToken.None);
            Assert.NotNull(row);
            Assert.Null(row.Job);
            Assert.Equal(subject, row.Subject);
            Assert.Equal(subject, OperationMapper.FromExecution(row).Metadata?.Subject);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    public sealed class ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : DbContext(options)
    {
        public DbSet<SchemataJob> Jobs => Set<SchemataJob>();
        public DbSet<SchemataJobExecution> Executions => Set<SchemataJobExecution>();
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task ScheduleSendAsync_CapturesTriggerArgs_And_ReturnsMatchingOperation()
    {
        JobContext? staged = null;
        CancellationToken capturedToken = default;
        var uid         = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.Parse("2025-01-15T09:30:00+02:00");
        var context     = new PushContext("payload", new TopicTarget("alerts")) { Subject = "reports/daily" };
        using var cts   = new CancellationTokenSource();
        var execution = new SchemataJobExecution {
            Uid          = uid,
            CanonicalName = $"operations/{uid:n}",
            Method       = "send",
            State        = ExecutionState.Pending,
        };

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<PushDispatchJob>(
                It.IsAny<JobContext>(),
                It.IsAny<CancellationToken>()))
            .Callback((JobContext ctx, CancellationToken token) => {
                staged       = ctx;
                capturedToken = token;
            })
            .ReturnsAsync(execution);

        using var services = BuildServices(scheduler.Object);
        var service = new ScheduledPushService(services.GetRequiredService<IRequestDispatcher>());

        var operation = await service.ScheduleSendAsync(context, scheduledAt, cts.Token);

        Assert.NotNull(staged);

        Assert.NotNull(staged.ArgsJson);
        var roundTrip = JsonSerializer.Deserialize<PushContext>(staged.ArgsJson, SchemataJson.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal("payload", Assert.IsType<JsonElement>(roundTrip.Message).GetString());
        Assert.Equal("alerts", Assert.IsType<TopicTarget>(roundTrip.Target).Topic);
        Assert.Equal("reports/daily", staged.Subject);
        Assert.Equal("reports/daily", roundTrip.Subject);
        Assert.Equal(cts.Token, capturedToken);

        Assert.Equal("send", staged.Method);

        Assert.NotNull(staged.ExecutionUid);
        Assert.NotEqual(Guid.Empty, staged.ExecutionUid.Value);

        Assert.NotNull(staged.StartTime);
        Assert.Equal(DateTimeKind.Utc, staged.StartTime.Value.Kind);
        Assert.Equal(scheduledAt.UtcDateTime, staged.StartTime.Value);


        Assert.Equal(execution.CanonicalName, operation.CanonicalName);
        Assert.False(operation.Done);
    }

    [Fact]
    [Trait("Layer", "Integration")]
    public async Task ScheduleSendAsync_Without_Time_Leaves_StartTime_Null() {
        JobContext? staged = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(value => value.TriggerAsync<PushDispatchJob>(
                            It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback((JobContext context, CancellationToken _) => staged = context)
                 .ReturnsAsync(new SchemataJobExecution {
                     Uid           = Guid.NewGuid(),
                     CanonicalName = $"operations/{Guid.NewGuid():n}",
                     State         = ExecutionState.Pending,
                 });
        using var services = BuildServices(scheduler.Object);
        var service = new ScheduledPushService(services.GetRequiredService<IRequestDispatcher>());

        await service.ScheduleSendAsync(
            new("payload", new TopicTarget("alerts")),
            null,
            CancellationToken.None);

        Assert.NotNull(staged);
        Assert.Null(staged.StartTime);
        Assert.Null(staged.Subject);
    }
    private static ServiceProvider BuildServices(IScheduler scheduler) {
        var services = new ServiceCollection();
        services.AddSingleton(scheduler);
        services.AddSchemataPush();
        services.AddSingleton<IRequestHandler<SchedulePushRequest, Abstractions.Resource.Operation>,
            SchedulePushHandler>();
        return services.BuildServiceProvider();
    }

}
