using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Common;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Skeleton.Drivers;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;
using Schemata.Report.Skeleton.Entities;
using Schemata.Report.Skeleton.Models;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class ReportActorIdentityFixture(IHost host, string path) : IAsyncDisposable
{
    internal IServiceProvider Services => host.Services;

    internal ReportQueryGate Gate => Services.GetRequiredService<ReportQueryGate>();

    internal static async Task<ReportActorIdentityFixture> CreateAsync(Action<IServiceCollection>? configure = null) {
        var path = Path.Combine(Path.GetTempPath(), $"report-actor-identity-{Guid.NewGuid():n}.db");
        var hostBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var services = hostBuilder.Services;
        services.AddLogging();
        var ticks = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).Ticks;
        var clock = new Mock<TimeProvider> { CallBase = true };
        clock.Setup(value => value.GetUtcNow()).Returns(() => new DateTimeOffset(Interlocked.Increment(ref ticks), TimeSpan.Zero));
        services.AddSingleton<TimeProvider>(clock.Object);
        services.AddDbContextFactory<ReportActorIdentityDbContext>(options => options
            .UseSqlite($"Data Source={path};Pooling=False")
            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataReport, EfCoreRepository<ReportActorIdentityDbContext, SchemataReport>>();
        services.AddRepository<SchemataReportSnapshot, EfCoreRepository<ReportActorIdentityDbContext, SchemataReportSnapshot>>();
        services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<ReportActorIdentityDbContext, SchemataReportSnapshotChunk>>();
        services.AddRepository<SchemataJob, EfCoreRepository<ReportActorIdentityDbContext, SchemataJob>>();
        services.AddRepository<SchemataJobExecution, EfCoreRepository<ReportActorIdentityDbContext, SchemataJobExecution>>();
        services.AddScoped<IUnitOfWork<ReportActorIdentityDbContext>, EfCoreUnitOfWork<ReportActorIdentityDbContext>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshot>, AdviceAddResourceName<SchemataReportSnapshot>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataReportSnapshotChunk>, AdviceAddResourceName<SchemataReportSnapshotChunk>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
        services.AddSingleton<ReportQueryGate>();

        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight(insight => {
            insight.AddSource("daily-rows", "controlled");
            insight.AddSource("other-rows", "controlled");
            insight.AddSourceDriver<ControlledReportDriver>("controlled");
        });
        builder.UseActor();
        services.AddSchemataSchedulingRepositoryStore();
        builder.UseScheduling();
        builder.UseReport(options => options.ChunkSize = 1).UseActor();
        builder.Invoke(services);
        configure?.Invoke(services);
        var host = hostBuilder.Build();
        var fixture = new ReportActorIdentityFixture(host, path);
        try {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReportActorIdentityDbContext>();
            await db.Database.EnsureCreatedAsync();
            foreach (var name in new[] { "daily", "other" }) {
                db.Add(new SchemataReport {
                    Uid = Guid.NewGuid(),
                    Name = name,
                    CanonicalName = $"reports/{name}",
                    Definition = JsonSerializer.Serialize(new QueryInsightRequest {
                        Sources = [new("row", $"{name}-rows")],
                    }, SchemataJson.Default),
                    Retention = new ReportRetention { MaxCount = 2 },
                });
            }
            await db.SaveChangesAsync();
            await host.StartAsync();
            var initializer = host.Services.GetServices<IHostedService>().OfType<SchedulingInitializer>().Single();
            var startup = initializer.ExecuteTask
                          ?? throw new InvalidOperationException("Scheduling startup did not create its initialization task.");
            await startup;
            return fixture;
        } catch (Exception failure) {
            try {
                await fixture.DisposeAsync();
            } catch (Exception cleanup) {
                throw new AggregateException(failure, cleanup);
            }
            throw;
        }
    }

    internal async Task ExecuteAsync(Func<Task> test) {
        try {
            await test();
        } catch (Exception failure) {
            try {
                await DisposeAsync();
            } catch (Exception cleanup) {
                throw new AggregateException(failure, cleanup);
            }
            throw;
        }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync() {
        Gate.Release();
        List<Exception>? failures = null;
        try {
            await host.StopAsync();
        } catch (Exception error) {
            (failures ??= []).Add(error);
        }
        try {
            if (host is IAsyncDisposable disposable) await disposable.DisposeAsync();
            else host.Dispose();
        } catch (Exception error) {
            (failures ??= []).Add(error);
        }
        try {
            File.Delete(path);
        } catch (Exception error) {
            (failures ??= []).Add(error);
        }
        if (failures is { Count: 1 }) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is not null) throw new AggregateException(failures);
    }
}

internal sealed class ReportActorIdentityDbContext(DbContextOptions<ReportActorIdentityDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) {
        builder.Entity<SchemataReport>(entity => {
            entity.HasKey(report => report.Uid);
            entity.OwnsOne(report => report.Retention);
        });
        builder.Entity<SchemataReportSnapshot>().HasKey(snapshot => snapshot.Uid);
        builder.Entity<SchemataReportSnapshotChunk>().HasKey(chunk => chunk.Uid);
        builder.Entity<SchemataJob>().HasKey(job => job.Uid);
        builder.Entity<SchemataJobExecution>().HasKey(execution => execution.Uid);
    }
}

internal sealed class ReportQueryGate
{
    private readonly TaskCompletionSource<bool> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _overlap = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _other = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private int _maximum;
    private int _runs;

    internal Task First => _first.Task;
    internal Task Overlap => _overlap.Task;
    internal Task Other => _other.Task;
    internal int Maximum => Volatile.Read(ref _maximum);

    internal void Release() => _release.TrySetResult(true);

    internal async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> RowsAsync(
        bool daily, [EnumeratorCancellation] CancellationToken ct) {
        if (!daily) {
            _other.TrySetResult(true);
            yield return new Dictionary<string, object?> { ["value"] = 100 };
            yield break;
        }

        var active = Interlocked.Increment(ref _active);
        int previous;
        do {
            previous = Volatile.Read(ref _maximum);
        } while (active > previous && Interlocked.CompareExchange(ref _maximum, active, previous) != previous);
        if (active > 1) _overlap.TrySetResult(true);
        var run = Interlocked.Increment(ref _runs);
        _first.TrySetResult(true);
        try {
            await _release.Task.WaitAsync(ct);
            yield return new Dictionary<string, object?> { ["value"] = run };
        } finally {
            Interlocked.Decrement(ref _active);
        }
    }
}

internal sealed class ControlledReportDriver(ReportQueryGate gate) : ISourceDriver
{
    public string Name => "controlled";

    public DriverCapabilities Capabilities => DriverCapabilities.Filter | DriverCapabilities.Project
                                           | DriverCapabilities.Order | DriverCapabilities.Nested;

    public ValueTask<ISourceResult> ExecuteAsync(
        SubPlan subPlan, QueryInsightRequest request, ClaimsPrincipal? principal, CancellationToken ct = default) {
        return ValueTask.FromResult<ISourceResult>(new RepositorySourceResult(
            gate.RowsAsync(request.Sources[0].Name == "daily-rows", ct), []));
    }
}
