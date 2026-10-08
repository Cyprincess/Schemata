using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Report.Scheduling;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Integration.Tests.Fixtures;

internal sealed class ReportScheduleLifecycleHost(WebApplication app, string path) : IAsyncDisposable
{
    public IServiceProvider Services => app.Services;

    public ScheduleMutationControl Control => Services.GetRequiredService<ScheduleMutationControl>();

    public static async Task<ReportScheduleLifecycleHost> CreateAsync(bool configured = false, Action<IServiceCollection>? configure = null) {
        var path = Path.Combine(Path.GetTempPath(), $"report-schedule-{Guid.NewGuid():n}.db");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            Args = ["--urls", "http://127.0.0.1:0"],
        });
        builder.Services.AddSingleton<IStartupFilter, ScheduleRoutingStartup>();
        builder.UseSchemata(schema => {
            schema.Services.AddDbContextFactory<ScheduleDbContext>(options => options
                .UseSqlite($"Data Source={path};Pooling=False")
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            schema.Services.AddRepository<ScheduleReport, EfCoreRepository<ScheduleDbContext, ScheduleReport>>();
            schema.Services.AddRepository<SchemataReportSnapshot, EfCoreRepository<ScheduleDbContext, SchemataReportSnapshot>>();
            schema.Services.AddRepository<SchemataReportSnapshotChunk, EfCoreRepository<ScheduleDbContext, SchemataReportSnapshotChunk>>();
            schema.Services.AddRepository<SchemataJob, EfCoreRepository<ScheduleDbContext, SchemataJob>>();
            schema.Services.AddRepository<SchemataJobExecution, EfCoreRepository<ScheduleDbContext, SchemataJobExecution>>();
            schema.Services.AddScoped<IUnitOfWork<ScheduleDbContext>, EfCoreUnitOfWork<ScheduleDbContext>>();
            schema.Services.AddSingleton<ScheduleMutationControl>();
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, AdviceAddResourceName<SchemataJob>>());
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJobExecution>, AdviceAddResourceName<SchemataJobExecution>>());
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<SchemataJob>, RejectJobWrite>());
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryAddAdvisor<ScheduleReport>, BlockReportCreate>());
            schema.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<ScheduleReport>, BlockReportUpdate>());
            schema.Services.AddSchemataSchedulingRepositoryStore();
            schema.UseScheduling();
            var reports = schema.UseReport<ScheduleReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>();
            if (configured) {
                reports.Define("configured", definition => definition.Periodic(interval: TimeSpan.FromDays(1)));
            }
            reports.UseScheduling();
            schema.UseReport<ScheduleReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>().UseScheduling();
        });
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        var host = new ReportScheduleLifecycleHost(app, path);
        try {
            await using (var scope = app.Services.CreateAsyncScope()) {
                await scope.ServiceProvider.GetRequiredService<ScheduleDbContext>().Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            return host;
        } catch {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task InitializeAsync() {
        await Services.GetRequiredService<ReportSchedulingInitializer>().StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync() {
        try {
            await app.StopAsync();
        } finally {
            await app.DisposeAsync();
            File.Delete(path);
        }
    }
}

internal sealed class ScheduleRoutingStartup : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
        return app => {
            app.UseRouting();
            next(app);
        };
    }
}

[CanonicalName("reports/{report}")]
public sealed class ScheduleReport : SchemataReport, ISoftDelete
{
    public DateTime? DeleteTime { get; set; }

    public DateTime? PurgeTime { get; set; }
}

public sealed class ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) {
        builder.Entity<ScheduleReport>(entity => {
            entity.HasKey(report => report.Uid);
            entity.OwnsOne(report => report.Retention);
        });
        builder.Entity<SchemataReportSnapshot>().HasKey(snapshot => snapshot.Uid);
        builder.Entity<SchemataReportSnapshotChunk>().HasKey(chunk => chunk.Uid);
        builder.Entity<SchemataJob>().HasKey(job => job.Uid);
        builder.Entity<SchemataJobExecution>().HasKey(execution => execution.Uid);
    }
}

internal sealed class ScheduleMutationControl
{
    public bool BlockCreate { get; set; }

    public bool BlockUpdate { get; set; }

    public string? RejectedPrefix { get; set; }
}

internal sealed class BlockReportCreate(ScheduleMutationControl control) : IRepositoryAddAdvisor<ScheduleReport>
{
    public int Order => int.MaxValue;

    public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<ScheduleReport> repository, ScheduleReport entity, CancellationToken ct) {
        return Task.FromResult(control.BlockCreate ? AdviseResult.Block : AdviseResult.Continue);
    }
}

internal sealed class BlockReportUpdate(ScheduleMutationControl control) : IRepositoryUpdateAdvisor<ScheduleReport>
{
    public int Order => int.MaxValue;

    public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<ScheduleReport> repository, ScheduleReport entity, CancellationToken ct) {
        return Task.FromResult(control.BlockUpdate ? AdviseResult.Block : AdviseResult.Continue);
    }
}

internal sealed class RejectJobWrite(ScheduleMutationControl control) : IRepositoryAddAdvisor<SchemataJob>
{
    public int Order => int.MaxValue;

    public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataJob> repository, SchemataJob entity, CancellationToken ct) {
        if (control.RejectedPrefix is { } prefix && entity.Key?.StartsWith(prefix, StringComparison.Ordinal) == true) {
            throw new InvalidOperationException($"Job write rejected: {entity.Key}");
        }
        return Task.FromResult(AdviseResult.Continue);
    }
}
